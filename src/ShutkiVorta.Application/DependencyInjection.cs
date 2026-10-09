using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Behaviors;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Wholesale;

namespace ShutkiVorta.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Business settings live in the database (Admin → Settings) and can change while the site runs.
        services.AddLiveOptions<BusinessOptions>(configuration, BusinessOptions.SectionName);
        services.AddLiveOptions<OrderingOptions>(configuration, OrderingOptions.SectionName);
        services.AddLiveOptions<EmailOptions>(configuration, EmailOptions.SectionName);
        services.AddLiveOptions<SiteOptions>(configuration, SiteOptions.SectionName);
        services.AddLiveOptions<WholesaleOptions>(configuration, WholesaleOptions.SectionName);
        services.AddLiveOptions<AccountOptions>(configuration, AccountOptions.SectionName);
        services.AddLiveOptions<RateLimitingOptions>(configuration, RateLimitingOptions.SectionName);

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<OrderSchedule>();
        services.AddScoped<StandingOrderScheduler>();

        return services;
    }

    /// <summary>
    /// Binds <typeparamref name="T"/> to its configuration section and makes <see cref="IOptions{T}"/> resolve per request
    /// (via <see cref="IOptionsSnapshot{T}"/>), so handlers and pages always see the latest saved values.
    /// Singletons must take <see cref="IOptionsMonitor{T}"/> instead.
    /// </summary>
    private static void AddLiveOptions<T>(this IServiceCollection services, IConfiguration configuration, string sectionName) where T : class
    {
        services.Configure<T>(configuration.GetSection(sectionName));
        services.AddScoped<IOptions<T>>(sp => sp.GetRequiredService<IOptionsSnapshot<T>>());
    }
}
