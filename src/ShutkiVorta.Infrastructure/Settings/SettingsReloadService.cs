using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// When the site runs on several servers, settings saved on one of them reach the others within 30 seconds.
/// (On the server where they were saved they apply immediately.)
/// </summary>
internal sealed class SettingsReloadService(SettingsRepository repository, DatabaseSettingsSource source, ILogger<SettingsReloadService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        long? seen = null;
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    var revision = await repository.GetGlobalRevisionAsync(stoppingToken);
                    if (seen is not null && revision != seen && source.IsAttached)
                    {
                        logger.LogInformation("Settings were changed on another server; reloading");
                        source.Reload();
                    }

                    seen = revision;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Checking for settings changes failed; will retry");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
