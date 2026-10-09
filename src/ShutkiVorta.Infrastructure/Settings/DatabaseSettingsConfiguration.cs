using Microsoft.Extensions.Configuration;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// Configuration source for the settings stored in the database. It is added to the application's configuration after the
/// database has been migrated (so it is the last, highest-priority source) and reloaded whenever settings are saved.
/// Secret values are passed through still encrypted; they are decrypted when options are bound.
/// </summary>
internal sealed class DatabaseSettingsSource(Func<IReadOnlyDictionary<string, string?>> load) : IConfigurationSource
{
    private DatabaseSettingsProvider? _provider;

    public bool IsAttached => _provider is not null;

    public IConfigurationProvider Build(IConfigurationBuilder builder) => _provider ??= new DatabaseSettingsProvider(load);

    /// <summary>Re-reads the database and notifies options monitors (no-op until the source is attached).</summary>
    public void Reload() => _provider?.Reload();
}

internal sealed class DatabaseSettingsProvider(Func<IReadOnlyDictionary<string, string?>> load) : ConfigurationProvider
{
    public override void Load() => Data = new Dictionary<string, string?>(load(), StringComparer.OrdinalIgnoreCase);

    public void Reload()
    {
        Load();
        OnReload();
    }
}
