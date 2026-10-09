using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Web.Infrastructure;

/// <summary>
/// Limits how often one IP address can submit the public forms that send emails or create accounts, so they cannot be
/// used to spam the kitchen or third parties through our mail server. The limits are set in Admin → Settings → Spam protection
/// (default: 10 submissions per form per 10 minutes) and apply immediately.
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

    public static IServiceCollection AddFormRateLimiting(this IServiceCollection services)
    {
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

                // The limits are part of the partition key, so a change in the settings starts fresh windows right away.
                var limits = context.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitingOptions>>().CurrentValue;
                var permits = Math.Max(1, limits.FormPostsPerWindow);
                var minutes = Math.Max(1, limits.WindowMinutes);
                var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter($"{client}|{path.ToLowerInvariant()}|{permits}/{minutes}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(minutes),
                    QueueLimit = 0,
                });
            });
        });
    }
}
