using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Staff;

/// <summary>
/// The Permissions page and a person's custom permissions are edited as one level per module (e.g. orders = "manage");
/// these turn the chosen levels into permission keys and back.
/// </summary>
public static class PermissionLevels
{
    /// <summary>The level of every module in a set of permissions (module key → level key), e.g. to pre-fill the selects.</summary>
    public static IReadOnlyDictionary<string, string> Of(IReadOnlySet<string> permissions) =>
        PermissionCatalog.Modules.ToDictionary(m => m.Key, m => PermissionCatalog.LevelFor(m, permissions).Key, StringComparer.Ordinal);

    /// <summary>Every module and level named exists in the catalog.</summary>
    public static bool AreKnown(IReadOnlyDictionary<string, string>? levels) =>
        levels is not null && levels.All(l => PermissionCatalog.Modules.Any(m => m.Key == l.Key && m.Levels.Any(level => level.Key == l.Value)));

    /// <summary>
    /// The permissions the chosen levels grant. A module the form did not send (e.g. one added to the site after the page was
    /// opened) keeps its level in <paramref name="current"/>.
    /// </summary>
    public static IReadOnlySet<string> Grants(IReadOnlyDictionary<string, string> levels, IReadOnlySet<string> current)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in PermissionCatalog.Modules)
        {
            var level = levels.TryGetValue(module.Key, out var key) ? module.Level(key) : PermissionCatalog.LevelFor(module, current);
            granted.UnionWith(level.Grants);
        }

        return PermissionCatalog.Normalize(granted);
    }
}
