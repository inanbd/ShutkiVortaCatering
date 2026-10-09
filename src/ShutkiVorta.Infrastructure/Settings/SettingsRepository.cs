using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Settings;

/// <summary>
/// Reads and writes the AppSettings, AppSettingsSections and AppSettingsHistory tables. Every save is recorded in the history
/// and makes the new values live at once.
/// </summary>
internal sealed class SettingsRepository(
    IDbConnectionFactory connections,
    ISqlDialect dialect,
    SettingsSecretProtector protector,
    SettingsConfiguration settings,
    IDateTimeProvider clock) : ISettingsStore
{
    private const int HistoryShown = 25;
    private const string SecretChanged = "(changed)";
    private const string SecretRemoved = "(removed)";

    public async Task<StoredSection> GetSectionAsync(string section, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // The revision is read BEFORE the values: if a save slips in between, the form carries an older revision than its
        // values and saving it reports a conflict, instead of silently overwriting the other admin's change.
        var state = await connection.QuerySingleOrDefaultAsync<SectionRow>(new CommandDefinition(
            "SELECT Section, Revision, UpdatedAtUtc, UpdatedBy, NeedsReview, ImportHash FROM AppSettingsSections WHERE Section = @Section",
            new { Section = section },
            cancellationToken: cancellationToken));
        var rows = await ReadSectionAsync(connection, null, section, cancellationToken);
        var history = await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            dialect.Page("SELECT [Key], OldValue, NewValue, ChangedBy, ChangedAtUtc FROM AppSettingsHistory WHERE Section = @Section ORDER BY ChangedAtUtc DESC, Id DESC"),
            new { Section = section, Skip = 0, Take = HistoryShown },
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

        return new StoredSection(values, secrets, state?.Revision ?? 0, state?.UpdatedAtUtc, state?.UpdatedBy)
        {
            NeedsReview = state?.NeedsReview ?? false,
            History = [.. history.Select(h => new SettingChange(h.Key, h.OldValue, h.NewValue, h.ChangedBy, h.ChangedAtUtc))],
        };
    }

    public async Task<int> SaveSectionAsync(
        string section,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, SecretChange> secrets,
        int expectedRevision,
        string changedBy,
        CancellationToken cancellationToken = default)
    {
        var revision = await WriteSectionAsync(section, values, secrets, expectedRevision, changedBy, needsReview: false, importHash: null, cancellationToken);
        await settings.Provider.ReloadAsync(CancellationToken.None);
        return revision;
    }

    /// <summary>Writes a section during import (no revision check). The provider is reloaded by the caller.</summary>
    /// <param name="importHash">Fingerprint of re-imported configuration; null keeps the stored one.</param>
    public Task ImportSectionAsync(
        string section,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, SecretChange> secrets,
        string changedBy,
        bool needsReview,
        string? importHash,
        CancellationToken cancellationToken = default) =>
        WriteSectionAsync(section, values, secrets, expectedRevision: null, changedBy, needsReview, importHash, cancellationToken);

    /// <summary>Total of all section revisions: changes whenever anything is saved (used by other servers to reload).</summary>
    public async Task<long> GetGlobalRevisionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(Revision), 0) FROM AppSettingsSections", cancellationToken: cancellationToken));
    }

    /// <summary>The section's import fingerprint, or null when the section does not exist yet.</summary>
    public async Task<(bool Exists, string? ImportHash)> GetImportStateAsync(string section, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var state = await connection.QuerySingleOrDefaultAsync<SectionRow>(new CommandDefinition(
            "SELECT Section, Revision, UpdatedAtUtc, UpdatedBy, NeedsReview, ImportHash FROM AppSettingsSections WHERE Section = @Section",
            new { Section = section },
            cancellationToken: cancellationToken));
        return (state is not null, state?.ImportHash);
    }

    /// <summary>Whether the site already has business data (so a section imported from defaults deserves a review).</summary>
    public async Task<bool> HasBusinessDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT (SELECT COUNT(*) FROM Orders) + (SELECT COUNT(*) FROM StandingOrders) + (SELECT COUNT(*) FROM CateringInquiries)",
            cancellationToken: cancellationToken)) > 0;
    }

    /// <summary>Forgets which configuration the re-import switch applied (called while the switch is off).</summary>
    public async Task ClearImportHashesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE AppSettingsSections SET ImportHash = NULL WHERE ImportHash IS NOT NULL", cancellationToken: cancellationToken));
    }

    /// <summary>All stored settings for the configuration provider (secrets still encrypted), with the revision they belong to.</summary>
    public async Task<SettingsSnapshot> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);

        // Revision first: if a save slips in between, the values are newer than the revision and the next check reloads again.
        var revision = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COALESCE(SUM(Revision), 0) FROM AppSettingsSections", cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<SettingRow>(new CommandDefinition("SELECT [Key], Value FROM AppSettings", cancellationToken: cancellationToken));
        return new SettingsSnapshot(rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase), revision);
    }

    private async Task<int> WriteSectionAsync(
        string section,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, SecretChange> secrets,
        int? expectedRevision,
        string changedBy,
        bool needsReview,
        string? importHash,
        CancellationToken cancellationToken)
    {
        if (ManagedSettings.Find(section) is null)
        {
            throw new ArgumentException($"'{section}' is not a database-managed settings section.", nameof(section));
        }

        var foreign = values.Keys.Concat(secrets.Keys).FirstOrDefault(k => !k.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase));
        if (foreign is not null)
        {
            throw new ArgumentException($"Key '{foreign}' does not belong to section '{section}'.", nameof(values));
        }

        var now = clock.UtcNow;
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var revision = await BumpRevisionAsync(connection, transaction, section, expectedRevision, now, changedBy, needsReview, importHash, cancellationToken);
        var existing = (await ReadSectionAsync(connection, transaction, section, cancellationToken))
            .ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        var rows = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values.Where(kv => !ManagedSettings.IsSecret(kv.Key)))
        {
            rows[key] = value;
        }

        // Secrets that are not being changed keep their stored (encrypted) value.
        foreach (var (key, value) in existing.Where(kv => ManagedSettings.IsSecret(kv.Key) && !secrets.ContainsKey(kv.Key)))
        {
            rows[key] = value;
        }

        foreach (var (key, change) in secrets.Where(s => !s.Value.Clear && !string.IsNullOrEmpty(s.Value.NewValue)))
        {
            rows[key] = SettingsSecretProtector.IsProtected(change.NewValue) ? change.NewValue : protector.Protect(change.NewValue!);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM AppSettings WHERE [Key] LIKE @Prefix", new { Prefix = section + ":%" }, transaction, cancellationToken: cancellationToken));
        if (rows.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO AppSettings ([Key], Value, UpdatedAtUtc, UpdatedBy) VALUES (@Key, @Value, @UpdatedAtUtc, @UpdatedBy)",
                rows.Select(r => new { r.Key, r.Value, UpdatedAtUtc = now, UpdatedBy = changedBy }),
                transaction,
                cancellationToken: cancellationToken));
        }

        var changes = Diff(existing, rows, secrets).ToList();
        if (changes.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO AppSettingsHistory (Section, [Key], OldValue, NewValue, ChangedBy, ChangedAtUtc) VALUES (@Section, @Key, @OldValue, @NewValue, @ChangedBy, @ChangedAtUtc)",
                changes.Select(c => new { Section = section, c.Key, c.OldValue, c.NewValue, ChangedBy = changedBy, ChangedAtUtc = now }),
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return revision;
    }

    /// <summary>Per-setting differences; list items are compared as a whole list; secret values are never recorded.</summary>
    private static IEnumerable<(string Key, string? OldValue, string? NewValue)> Diff(
        IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after, IReadOnlyDictionary<string, SecretChange> secrets)
    {
        static string Group(string key)
        {
            var colon = key.LastIndexOf(':');
            return colon > 0 && int.TryParse(key[(colon + 1)..], out _) ? key[..colon] : key;
        }

        static string? Join(IReadOnlyDictionary<string, string?> source, string group, bool isList) =>
            isList
                ? string.Join(", ", source.Where(kv => Group(kv.Key).Equals(group, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => int.Parse(kv.Key[(kv.Key.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture))
                    .Select(kv => kv.Value))
                : source.GetValueOrDefault(group);

        var groups = before.Keys.Concat(after.Keys).GroupBy(Group, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var isList = group.Any(k => !k.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
            if (ManagedSettings.IsSecret(group.Key))
            {
                if (secrets.TryGetValue(group.Key, out var change))
                {
                    yield return (group.Key, before.ContainsKey(group.Key) ? "(saved)" : null, change.Clear ? SecretRemoved : SecretChanged);
                }

                continue;
            }

            var oldValue = Join(before, group.Key, isList);
            var newValue = Join(after, group.Key, isList);
            if (!string.Equals(oldValue ?? string.Empty, newValue ?? string.Empty, StringComparison.Ordinal))
            {
                yield return (group.Key, Truncate(oldValue), Truncate(newValue));
            }
        }
    }

    private static string? Truncate(string? value) => value is { Length: > 4000 } ? value[..4000] : value;

    private static async Task<List<SettingRow>> ReadSectionAsync(DbConnection connection, DbTransaction? transaction, string section, CancellationToken cancellationToken) =>
        (await connection.QueryAsync<SettingRow>(new CommandDefinition(
            "SELECT [Key], Value FROM AppSettings WHERE [Key] LIKE @Prefix",
            new { Prefix = section + ":%" },
            transaction,
            cancellationToken: cancellationToken))).AsList();

    private static async Task<int> BumpRevisionAsync(
        DbConnection connection,
        DbTransaction transaction,
        string section,
        int? expectedRevision,
        DateTime now,
        string changedBy,
        bool needsReview,
        string? importHash,
        CancellationToken cancellationToken)
    {
        var parameters = new { Section = section, Expected = expectedRevision, Now = now, By = changedBy, NeedsReview = needsReview, ImportHash = importHash };
        var updated = await connection.ExecuteAsync(new CommandDefinition(
            $"""
            UPDATE AppSettingsSections SET Revision = Revision + 1, UpdatedAtUtc = @Now, UpdatedBy = @By, NeedsReview = @NeedsReview,
                ImportHash = COALESCE(@ImportHash, ImportHash)
            WHERE Section = @Section{(expectedRevision is null ? string.Empty : " AND Revision = @Expected")}
            """,
            parameters,
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
                "INSERT INTO AppSettingsSections (Section, Revision, UpdatedAtUtc, UpdatedBy, NeedsReview, ImportHash) VALUES (@Section, 1, @Now, @By, @NeedsReview, @ImportHash)",
                parameters,
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
        public bool NeedsReview { get; init; }
        public string? ImportHash { get; init; }
    }

    private sealed class HistoryRow
    {
        public string Key { get; init; } = string.Empty;
        public string? OldValue { get; init; }
        public string? NewValue { get; init; }
        public string ChangedBy { get; init; } = string.Empty;
        public DateTime ChangedAtUtc { get; init; }
    }
}
