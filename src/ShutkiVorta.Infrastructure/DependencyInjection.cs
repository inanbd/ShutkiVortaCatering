using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Infrastructure.Email;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.Infrastructure.Persistence;
using ShutkiVorta.Infrastructure.Persistence.Repositories;
using ShutkiVorta.Infrastructure.Persistence.Seed;
using ShutkiVorta.Infrastructure.Services;
using ShutkiVorta.Infrastructure.Settings;
using ShutkiVorta.Infrastructure.Wholesale;

namespace ShutkiVorta.Infrastructure;

public static class DependencyInjection
{
    /// <param name="configuration">The application configuration (appsettings.json, environment variables).</param>
    /// <param name="settings">The database-backed business settings, also passed to AddApplication.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment, SettingsConfiguration settings)
    {
        // ---- Database (SQLite or SQL Server, selected by Database:Provider) ----
        DapperConfiguration.Configure();
        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        database.ConnectionString = ResolveConnectionString(configuration, database.Provider, environment.ContentRootPath);

        services.AddSingleton(database);
        services.AddSingleton<DbConnectionFactory>();
        services.AddSingleton<IDbConnectionFactory>(sp => sp.GetRequiredService<DbConnectionFactory>());
        services.AddSingleton<IDatabaseInfo>(sp => sp.GetRequiredService<DbConnectionFactory>());
        services.AddSingleton<ISqlDialect>(database.Provider == DatabaseProvider.Sqlite ? new SqliteDialect() : new SqlServerDialect());

        services.AddScoped<IMenuItemRepository, MenuItemRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ICateringInquiryRepository, CateringInquiryRepository>();
        services.AddScoped<IStandingOrderRepository, StandingOrderRepository>();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<MigrationRunner>();
        services.AddScoped<DatabaseSeeder>();
        services.AddScoped<DatabaseInitializer>();

        // ---- Identity (custom Dapper stores, no Entity Framework) ----
        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                // Whether the email must be confirmed is a database setting, checked at sign-in by SettingsUserConfirmation.
                options.SignIn.RequireConfirmedAccount = true;
            })
            .AddUserStore<DapperUserStore>()
            .AddRoleStore<DapperRoleStore>()
            .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserConfirmation<ApplicationUser>, SettingsUserConfirmation>();
        services.AddScoped<IIdentityService, IdentityService>();

        // ---- Email (durable outbox + background dispatcher) ----
        services.AddSingleton<EmailDispatchSignal>();
        services.AddScoped<EmailOutboxRepository>();
        services.AddScoped<IEmailLog>(sp => sp.GetRequiredService<EmailOutboxRepository>());
        services.AddScoped<IEmailService, OutboxEmailService>();
        services.AddScoped<IEmailTransport, EmailTransport>();
        services.AddSingleton<IEmailDiagnostics, EmailDiagnostics>();
        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddHostedService<EmailDispatcher>();

        // ---- Business settings stored in the database (Admin → Settings) ----
        services.AddSingleton(settings);
        services.AddSingleton<SettingsSecretProtector>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SettingsRepository>());
        services.AddSingleton<SettingsStartupReport>();
        services.AddSingleton<ISettingsDiagnostics>(sp => sp.GetRequiredService<SettingsStartupReport>());
        services.AddSingleton<ISettingsChangeSignal, SettingsChangeSignal>();
        services.AddSingleton<SettingsImporter>();
        services.AddSingleton<IPostConfigureOptions<EmailOptions>, EmailSecretsPostConfigure>();
        services.AddHostedService<SettingsReloadService>();

        // ---- Misc services ----
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IOrderNumberGenerator, OrderNumberGenerator>();
        services.AddScoped<IImageStorage, LocalImageStorage>();

        // ---- Restaurant standing orders: generate upcoming deliveries in the background ----
        services.AddHostedService<StandingOrderGenerator>();

        return services;
    }

    /// <summary>
    /// Creates the database (if needed), applies migrations, seeds data and loads the business settings from the database.
    /// Call once at startup, after the app is built.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
        }

        var settings = services.GetRequiredService<SettingsConfiguration>();
        if (settings.Provider.IsConnected)
        {
            return;
        }

        // First run: copy settings from appsettings.json / environment variables into the database (later runs only report
        // configured values that differ). From then on business settings are read from the database alone.
        var repository = services.GetRequiredService<SettingsRepository>();
        var importer = services.GetRequiredService<SettingsImporter>();
        await importer.ImportAsync(services.GetRequiredService<IConfiguration>(), cancellationToken);
        settings.Provider.Connect(repository.LoadAllAsync, services.GetRequiredService<ILoggerFactory>().CreateLogger<SettingsConfiguration>());
        if (!await settings.Provider.ReloadAsync(cancellationToken))
        {
            // Never serve customers with the built-in defaults instead of the saved settings (e.g. orders accepted while paused).
            throw new InvalidOperationException("The settings could not be loaded from the database; see the error logged above.");
        }

        importer.ReportIgnoredConfiguration();
    }

    private static string ResolveConnectionString(IConfiguration configuration, DatabaseProvider provider, string contentRoot)
    {
        var connectionString = configuration.GetConnectionString(provider.ToString());
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{provider}' is missing. Set it in appsettings.json or choose another Database:Provider.");
        }

        if (provider != DatabaseProvider.Sqlite)
        {
            return connectionString;
        }

        // Resolve relative SQLite paths against the content root so the database is found regardless of the working directory.
        var builder = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = true };
        if (builder.Mode != SqliteOpenMode.Memory && builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.Combine(contentRoot, builder.DataSource);
        }

        return builder.ToString();
    }
}
