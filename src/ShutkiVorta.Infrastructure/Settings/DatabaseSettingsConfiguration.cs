using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShutkiVorta.Application.Common.Settings;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// The configuration that business settings (Admin → Settings) are bound from. It contains ONLY the database, not
/// appsettings.json or environment variables, so the site uses exactly what admins saved — including lists that were
/// shortened or emptied. Files and environment variables only seed the database on first start (see SettingsImporter).
/// Create it once in Program.cs and pass it to AddApplication/AddInfrastructure.
/// </summary>
public sealed class SettingsConfiguration
{
    private SettingsConfiguration()
    {
        Provider = new DatabaseSettingsProvider();
        Root = new ConfigurationRoot([Provider]);
    }

    /// <summary>Bind managed options from this (it fires a reload token whenever the settings change).</summary>
    public IConfigurationRoot Root { get; }

    internal DatabaseSettingsProvider Provider { get; }

    public static SettingsConfiguration Create() => new();
}

/// <summary>All saved settings (secrets still encrypted) and the settings revision they belong to.</summary>
internal sealed record SettingsSnapshot(IReadOnlyDictionary<string, string?> Values, long Revision);

/// <summary>
/// Loads the AppSettings table. Until the database is ready it is empty (built-in defaults apply). A value that cannot be used
/// is dropped (and reported). If the database cannot be read, the last good values stay and the reload reports the failure,
/// so it is tried again.
/// Secret values are passed through still encrypted; they are decrypted when email options are bound.
/// </summary>
internal sealed class DatabaseSettingsProvider : ConfigurationProvider
{
    private readonly SemaphoreSlim _reloading = new(1, 1);
    private Func<CancellationToken, Task<SettingsSnapshot>>? _load;
    private ILogger? _logger;
    private IReadOnlyDictionary<string, string?> _current = new Dictionary<string, string?>();

    public bool IsConnected => _load is not null;

    /// <summary>The settings revision of the values in use, or null until they were loaded successfully.</summary>
    public long? LoadedRevision { get; private set; }

    /// <summary>The saved settings in use (secrets encrypted).</summary>
    public IReadOnlyDictionary<string, string?> Current => _current;

    /// <summary>Stored keys whose values could not be used at the last load.</summary>
    public IReadOnlyList<string> RejectedKeys { get; private set; } = [];

    public void Connect(Func<CancellationToken, Task<SettingsSnapshot>> load, ILogger logger)
    {
        _logger = logger;
        _load = load;
    }

    /// <summary>Nothing to do here: the data is loaded asynchronously by <see cref="ReloadAsync"/>.</summary>
    public override void Load()
    {
    }

    /// <summary>Reads the database and makes the values live (fires the reload token). False when the database could not be read.</summary>
    public async Task<bool> ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_load is null)
        {
            return false;
        }

        // One load at a time, so an older read can never overwrite a newer one.
        await _reloading.WaitAsync(cancellationToken);
        try
        {
            var snapshot = await _load(cancellationToken);
            var (clean, rejected) = SettingsSanitizer.Sanitize(snapshot.Values);
            Data = clean;
            _current = clean;
            RejectedKeys = rejected;
            LoadedRevision = snapshot.Revision;
            if (rejected.Count > 0)
            {
                _logger?.LogError(
                    "These stored settings have invalid values and are ignored (the default is used): {Keys}. Fix them in Admin → Settings.",
                    string.Join(", ", rejected));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "Loading settings from the database failed; the previous values stay in use until it works");
            return false;
        }
        finally
        {
            _reloading.Release();
        }

        OnReload();
        return true;
    }
}
