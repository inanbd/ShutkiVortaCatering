using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Wholesale;

namespace ShutkiVorta.Infrastructure.Wholesale;

/// <summary>
/// Creates the orders for active restaurant standing orders a few days ahead (Wholesale:GenerateDaysAhead), shortly after
/// startup and then every hour. Generation is idempotent, so running it often (or on several servers) is safe.
/// </summary>
internal sealed class StandingOrderGenerator(
    IServiceScopeFactory scopes,
    IOptionsMonitor<WholesaleOptions> options,
    ILogger<StandingOrderGenerator> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                if (options.CurrentValue.AutoGenerate)
                {
                    await RunOnceAsync(stoppingToken);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<StandingOrderScheduler>().GenerateAllAsync(stoppingToken);
            foreach (var error in report.Errors)
            {
                logger.LogWarning("Standing order generation problem: {Error}", error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Generating restaurant standing-order deliveries failed; will retry within the hour");
        }
    }
}
