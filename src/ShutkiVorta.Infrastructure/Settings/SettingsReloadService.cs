using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// When the site runs on several servers, settings saved on one of them reach the others within 30 seconds.
/// (On the server where they were saved they apply immediately.) A load that failed is tried again on the next check.
/// </summary>
internal sealed class SettingsReloadService(SettingsRepository repository, SettingsConfiguration settings, ILogger<SettingsReloadService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                try
                {
                    await CheckAsync(stoppingToken);
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

    /// <summary>Reloads when the saved settings are newer than the ones in use (or the last load failed).</summary>
    internal async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        if (!settings.Provider.IsConnected)
        {
            return false; // The database is still being prepared.
        }

        var revision = await repository.GetGlobalRevisionAsync(cancellationToken);
        if (revision == settings.Provider.LoadedRevision)
        {
            return false;
        }

        logger.LogInformation("The saved settings changed (revision {Revision}, in use {Loaded}); reloading", revision, settings.Provider.LoadedRevision);
        return await settings.Provider.ReloadAsync(cancellationToken);
    }
}
