using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Web.Infrastructure;
using ShutkiVorta.Web.Infrastructure.Security;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web;

public static class DependencyInjection
{
    /// <summary>The /Admin folder's policy: staff access (a staff role and a confirmed email), read from the database.</summary>
    public const string StaffPolicy = "Staff";

    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IAppUrls, AppUrls>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<CartService>();

        services.AddFormRateLimiting();

        services.AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/Admin", StaffPolicy);
                options.Conventions.AuthorizeFolder("/Account/Manage");
                options.Conventions.AuthorizeFolder("/Account/Orders");
                options.Conventions.AuthorizeFolder("/Account/RestaurantOrders");
                options.Conventions.AuthorizePage("/Restaurants/Order");
            })
            // Each admin page and handler states who may use it ([RequirePermission], [AdminOnly], [AnyStaff]). The filter
            // is added by type (a new instance per request, for the scoped ICurrentStaff) and runs before any page code.
            .AddMvcOptions(options => options.Filters.Add<PermissionPageFilter>(int.MinValue));

        services.AddRouting(options =>
        {
            options.LowercaseUrls = true;
            options.LowercaseQueryStrings = false;
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(StaffPolicy, policy => policy.RequireAuthenticatedUser().AddRequirements(new StaffRequirement()));
        services.AddScoped<IAuthorizationHandler, StaffRequirementHandler>();

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.AccessDeniedPath = "/account/access-denied";
            options.Cookie.Name = "sv_auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
        });

        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        // Persist data-protection keys so sign-in cookies and email tokens survive restarts.
        var keysPath = configuration["DataProtection:KeysPath"];
        keysPath = string.IsNullOrWhiteSpace(keysPath) ? Path.Combine(environment.ContentRootPath, "App_Data", "keys") : keysPath;
        services.AddDataProtection()
            .SetApplicationName("ShutkiVortaCatering")
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddAntiforgery(o => o.Cookie.Name = "sv_af");

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml", "application/xml"]);
        });

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var culture = new CultureInfo("en-US");
            options.DefaultRequestCulture = new RequestCulture(culture);
            options.SupportedCultures = [culture];
            options.SupportedUICultures = [culture];
        });

        return services;
    }
}
