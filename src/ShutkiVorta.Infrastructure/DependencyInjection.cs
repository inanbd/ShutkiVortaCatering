using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShutkiVorta.Application.Common.Interfaces;
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
using ShutkiVorta.Infrastructure.Wholesale;

namespace ShutkiVorta.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
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
                options.SignIn.RequireConfirmedEmail = configuration.GetValue("Identity:RequireConfirmedEmail", false);
            })
            .AddUserStore<DapperUserStore>()
            .AddRoleStore<DapperRoleStore>()
            .AddClaimsPrincipalFactory<ApplicationClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

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

    /// <summary>Creates the database (if needed), applies migrations and seeds data. Call once at startup.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
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
