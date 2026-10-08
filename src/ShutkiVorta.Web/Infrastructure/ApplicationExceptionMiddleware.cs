using ShutkiVorta.Application.Common.Exceptions;

namespace ShutkiVorta.Web.Infrastructure;

/// <summary>Turns application-layer exceptions into proper HTTP status codes (rendered by the themed error page).</summary>
public sealed class ApplicationExceptionMiddleware(RequestDelegate next, ILogger<ApplicationExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (NotFoundException ex) when (!context.Response.HasStarted)
        {
            logger.LogInformation("{Message}", ex.Message);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
        catch (ForbiddenAccessException ex) when (!context.Response.HasStarted)
        {
            logger.LogWarning("Forbidden: {Message} ({Path})", ex.Message, context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = context.User.Identity?.IsAuthenticated == true
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
        }
    }
}
