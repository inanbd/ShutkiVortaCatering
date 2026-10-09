using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ShutkiVorta.Web.Infrastructure;

/// <summary>
/// Limits how often one IP address can submit the public forms that send emails or create accounts, so they cannot be
/// used to spam the kitchen or third parties through our mail server. Configure with RateLimiting:FormPostsPerWindow
/// and RateLimiting:WindowMinutes (default: 10 submissions per form per 10 minutes).
/// </summary>
public static class FormRateLimiting
{
    private static readonly string[] LimitedPaths =
    [
        "/contact",
        "/catering",
        "/kitchen",
        "/restaurants/order",
        "/account/register",
        "/account/forgot-password",
        "/account/resend-confirmation",
    ];

    public static IServiceCollection AddFormRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permits = Math.Max(1, configuration.GetValue("RateLimiting:FormPostsPerWindow", 10));
        var window = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("RateLimiting:WindowMinutes", 10)));

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    "You've sent this form several times in a short while. Please wait a few minutes and try again, or give us a call.",
                    cancellationToken);
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
                if (!HttpMethods.IsPost(context.Request.Method) || !LimitedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"{client}|{path.ToLowerInvariant()}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = window,
                    QueueLimit = 0,
                });
            });
        });
    }
}
