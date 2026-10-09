using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>Reads and writes the AppSettings / AppSettingsSections tables. Every save makes the new values live at once.</summary>
internal sealed class SettingsRepository(
    IDbConnectionFactory connections,
    SettingsSecretProtector protector,
    DatabaseSettingsSource source,
    IDateTimeProvider clock) : ISettingsStore
{
    public async Task<StoredSection> GetSectionAsync(string section, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = (await connection.QueryAsync<SettingRow>(new CommandDefinition(
            "SELECT [Key], Value FROM AppSettings WHERE [Key] LIKE @Prefix",
            new { Prefix = section + ":%" },
            cancellationToken: cancellationToken))).AsList();
        var state = await connection.QuerySingleOrDefaultAsync<SectionRow>(new CommandDefinition(
            "SELECT Section, Revision, UpdatedAtUtc, UpdatedBy FROM AppSettingsSections WHERE Section = @Section",
            new { Section = section },
            cancellationToken: cancellationToken));

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var secrets = new Dictionary<string, SecretState>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (ManagedSettings.IsSecret(row.Key))
            {
                secrets[row.Key] = string.IsNullOrEmpty(row.Value) ? SecretState.Empty
                    : protector.TryUnprotect(row.Value, out _) ? SecretState.Saved : SecretState.Unreadable;
            }
            else
            {
                values[row.Key] = row.Value;
            }
        }

        return new StoredSection(values, secrets, state?.Revision ?? 0, state?.UpdatedAtUtc, state?.UpdatedBy);
    }

    public async Task<int> SaveSectionAsync(
        string section,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, SecretChange> secrets,
        int expectedRevision,
        string changedBy,
        CancellationToken cancellationToken = default)
    {
        var revision = await WriteSectionAsync(section, values, secrets, expectedRevision, changedBy, cancellationToken);
        source.Reload();
        return revision;
    }

    /// <summary>Total of all section revisions: changes whenever anything is saved (used by other servers to reload).</summary>
    public async Task<long> GetGlobalRevisionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(Revision), 0) FROM AppSettingsSections", cancellationToken: cancellationToken));
    }

    public async Task<bool> SectionExistsAsync(string section, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM AppSettingsSections WHERE Section = @Section", new { Section = section }, cancellationToken: cancellationToken)) > 0;
    }

    /// <summary>All stored settings for the configuration provider (secrets still encrypted).</summary>
    public IReadOnlyDictionary<string, string?> LoadAll()
    {
        using var connection = connections.OpenAsync().GetAwaiter().GetResult();
        return connection.Query<SettingRow>("SELECT [Key], Value FROM AppSettings")
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Writes a section without the revision check (first-run import). Creates the section's revision row.</summary>
    public async Task ImportSectionAsync(string section, IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, SecretChange> secrets, string changedBy, CancellationToken cancellationToken = default)
    {
        await WriteSectionAsync(section, values, secrets, expectedRevision: null, changedBy, cancellationToken);
    }

    private async Task<int> WriteSectionAsync(
        string section,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, SecretChange> secrets,
        int? expectedRevision,
        string changedBy,
        CancellationToken cancellationToken)
    {
        if (ManagedSettings.Find(section) is null)
        {
            throw new ArgumentException($"'{section}' is not a database-managed settings section.", nameof(section));
        }

        var foreign = values.Keys.FirstOrDefault(k => !k.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase));
        if (foreign is not null)
        {
            throw new ArgumentException($"Key '{foreign}' does not belong to section '{section}'.", nameof(values));
        }

        var now = clock.UtcNow;
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var revision = await BumpRevisionAsync(connection, transaction, section, expectedRevision, now, changedBy, cancellationToken);

        // Secrets that are not being changed keep their stored (encrypted) value.
        var keptSecrets = (await connection.QueryAsync<SettingRow>(new CommandDefinition(
                "SELECT [Key], Value FROM AppSettings WHERE [Key] LIKE @Prefix",
                new { Prefix = section + ":%" },
                transaction,
                cancellationToken: cancellationToken)))
            .Where(r => ManagedSettings.IsSecret(r.Key) && !secrets.ContainsKey(r.Key))
            .ToList();

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM AppSettings WHERE [Key] LIKE @Prefix", new { Prefix = section + ":%" }, transaction, cancellationToken: cancellationToken));

        var rows = values
            .Where(kv => !ManagedSettings.IsSecret(kv.Key))
            .Select(kv => new { Key = kv.Key, kv.Value })
            .Concat(keptSecrets.Select(r => new { r.Key, r.Value }))
            .Concat(secrets
                .Where(s => !s.Value.Clear && !string.IsNullOrEmpty(s.Value.NewValue))
                .Select(s => new { s.Key, Value = (string?)(SettingsSecretProtector.IsProtected(s.Value.NewValue) ? s.Value.NewValue : protector.Protect(s.Value.NewValue!)) }))
            .Select(r => new { r.Key, r.Value, UpdatedAtUtc = now, UpdatedBy = changedBy })
            .ToList();

        if (rows.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO AppSettings ([Key], Value, UpdatedAtUtc, UpdatedBy) VALUES (@Key, @Value, @UpdatedAtUtc, @UpdatedBy)",
                rows,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    private static async Task<int> BumpRevisionAsync(
        DbConnection connection, DbTransaction transaction, string section, int? expectedRevision, DateTime now, string changedBy, CancellationToken cancellationToken)
    {
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            expectedRevision is null
                ? "UPDATE AppSettingsSections SET Revision = Revision + 1, UpdatedAtUtc = @Now, UpdatedBy = @By WHERE Section = @Section"
                : "UPDATE AppSettingsSections SET Revision = Revision + 1, UpdatedAtUtc = @Now, UpdatedBy = @By WHERE Section = @Section AND Revision = @Expected",
            new { Section = section, Expected = expectedRevision, Now = now, By = changedBy },
            transaction,
            cancellationToken: cancellationToken));

        if (updated == 0)
        {
            var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM AppSettingsSections WHERE Section = @Section", new { Section = section }, transaction, cancellationToken: cancellationToken)) > 0;
            if (exists || expectedRevision is > 0)
            {
                throw new SettingsConflictException();
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO AppSettingsSections (Section, Revision, UpdatedAtUtc, UpdatedBy) VALUES (@Section, 1, @Now, @By)",
                new { Section = section, Now = now, By = changedBy },
                transaction,
                cancellationToken: cancellationToken));
        }

        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT Revision FROM AppSettingsSections WHERE Section = @Section", new { Section = section }, transaction, cancellationToken: cancellationToken));
    }

    private sealed class SettingRow
    {
        public string Key { get; init; } = string.Empty;
        public string? Value { get; init; }
    }

    private sealed class SectionRow
    {
        public string Section { get; init; } = string.Empty;
        public int Revision { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public string? UpdatedBy { get; init; }
    }
}
