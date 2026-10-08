using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ShutkiVorta.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowRequestThresholdMs = 750;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > SlowRequestThresholdMs)
            {
                logger.LogWarning("Slow request {RequestName} took {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                logger.LogDebug("Handled {RequestName} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
