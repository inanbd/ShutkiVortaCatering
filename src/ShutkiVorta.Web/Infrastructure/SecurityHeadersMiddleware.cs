namespace ShutkiVorta.Web.Infrastructure;

/// <summary>Adds conservative security headers to every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>The only pages that may ask for the device's location (clocking in and out records it).</summary>
    public const string LocationPath = "/admin/time-clock";

    public Task InvokeAsync(HttpContext context)
    {
        // Decided now: on a re-executed error page the path has changed by the time the response starts.
        var geolocation = context.Request.Path.StartsWithSegments(LocationPath, StringComparison.OrdinalIgnoreCase) ? "(self)" : "()";
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = $"camera=(), microphone=(), geolocation={geolocation}";
            return Task.CompletedTask;
        });

        return next(context);
    }
}
