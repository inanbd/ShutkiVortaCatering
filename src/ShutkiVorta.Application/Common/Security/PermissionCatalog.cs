namespace ShutkiVorta.Application.Common.Security;

/// <summary>One choice for a module on the Permissions page, e.g. "View &amp; change", and the permission keys it grants.</summary>
/// <param name="Key">Stable key of the level within its module, e.g. "view".</param>
public sealed record PermissionLevel(string Key, string Label, string Description, IReadOnlyList<string> Grants);

/// <summary>
/// An admin module shown as ONE select on the Permissions page. Its levels are cumulative (each grants everything the
/// previous one does), so the stored keys can never say "change" without "view".
/// </summary>
public sealed record PermissionModule(
    string Key,
    string Name,
    string Description,
    IReadOnlyList<PermissionLevel> Levels,
    string ManagerDefaultLevel,
    string EmployeeDefaultLevel)
{
    public PermissionLevel Level(string key) =>
        Levels.FirstOrDefault(l => l.Key == key) ?? throw new ArgumentException($"Module '{Key}' has no level '{key}'.", nameof(key));

    public PermissionLevel DefaultLevel(StaffRole role) => role switch
    {
        StaffRole.Admin => Levels[^1],
        StaffRole.Manager => Level(ManagerDefaultLevel),
        _ => Level(EmployeeDefaultLevel),
    };
}

/// <summary>
/// Every permission an admin can grant, grouped by module, with the Manager and Employee defaults. Admins always have
/// everything; the <see cref="AdminOnly"/> capabilities can never be granted to anyone else.
/// </summary>
public static class PermissionCatalog
{
    public const string NoAccess = "none";

    public static readonly IReadOnlyList<PermissionModule> Modules =
    [
        new("dashboard", "Dashboard", "The admin home page: order counts and what's coming up.",
        [
            new(NoAccess, "No access", "The time clock is their home page instead.", []),
            new("counts", "Counts", "Order counts and the upcoming and recent orders, without any money.", [Permissions.DashboardView]),
            new("sales", "With sales", "Also revenue, the sales chart and order totals.", [Permissions.DashboardView, Permissions.DashboardSales]),
        ], ManagerDefaultLevel: "sales", EmployeeDefaultLevel: "counts"),

        new("orders", "Orders", "Online and restaurant orders.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "See orders and their details.", [Permissions.OrdersView]),
            new("manage", "View & change", "Also change an order's status (which can email the customer), write kitchen notes and see the customer's tracking link.",
                [Permissions.OrdersView, Permissions.OrdersManage]),
        ], ManagerDefaultLevel: "manage", EmployeeDefaultLevel: "view"),

        new("restaurants", "Restaurant orders", "Restaurants' standing orders and their statements.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "See standing orders, deliveries and statements.", [Permissions.RestaurantsView]),
            new("manage", "View & change", "Also approve, decline, pause, resume or cancel, skip dates, change terms and prices, and create deliveries.",
                [Permissions.RestaurantsView, Permissions.RestaurantsManage]),
        ], ManagerDefaultLevel: "manage", EmployeeDefaultLevel: NoAccess),

        new("production", "Production plan", "How much of each vorta to make each day.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "See the production plan.", [Permissions.ProductionView]),
        ], ManagerDefaultLevel: "view", EmployeeDefaultLevel: "view"),

        new("menu", "Menu items", "The dishes on the website.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "See menu items and their prices.", [Permissions.MenuView]),
            new("manage", "View & change", "Also add, edit, hide and delete items and upload photos.", [Permissions.MenuView, Permissions.MenuManage]),
        ], ManagerDefaultLevel: "manage", EmployeeDefaultLevel: "view"),

        new("inventory", "Inventory", "Ingredient and supply purchases with their receipts.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "See purchases, spending and receipt photos.", [Permissions.InventoryView]),
            new("manage", "View & change", "Also add, edit and delete purchases, receipt photos and saved item names.",
                [Permissions.InventoryView, Permissions.InventoryManage]),
        ], ManagerDefaultLevel: "manage", EmployeeDefaultLevel: "view"),

        new("inquiries", "Inquiries", "Catering, contact and kitchen inquiries, with people's contact details.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "Read inquiries.", [Permissions.InquiriesView]),
            new("manage", "View & change", "Also mark inquiries handled or re-open them.", [Permissions.InquiriesView, Permissions.InquiriesManage]),
        ], ManagerDefaultLevel: "manage", EmployeeDefaultLevel: NoAccess),

        new("customers", "Customers", "Customer accounts and guest customers with their order history.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "See the customer list and customer details.", [Permissions.CustomersView]),
        ], ManagerDefaultLevel: "view", EmployeeDefaultLevel: NoAccess),

        new("timeclock", "Time clock", "Everyone can always clock in and out and see their own shifts.",
        [
            new("own", "Own shifts only", "Clock in and out, see their own shifts and hours.", []),
            new("team", "See team", "Also see everyone's shifts, locations and hours, and download them.", [Permissions.TimeClockViewAll]),
            new("fix", "See & fix team", "Also add, correct and void other people's shifts.", [Permissions.TimeClockViewAll, Permissions.TimeClockManage]),
        ], ManagerDefaultLevel: "fix", EmployeeDefaultLevel: "own"),

        new("staff", "Users", "The list of staff members.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View the staff list", "See who works here and their roles. Only Admins can change anyone's access.", [Permissions.StaffView]),
        ], ManagerDefaultLevel: "view", EmployeeDefaultLevel: NoAccess),

        new("emails", "Email log", "Every email the site sent, including customers' messages.",
        [
            new(NoAccess, "No access", "", []),
            new("view", "View", "Read the email log.", [Permissions.EmailsView]),
            new("manage", "View & retry", "Also send emails again.", [Permissions.EmailsView, Permissions.EmailsManage]),
        ], ManagerDefaultLevel: "view", EmployeeDefaultLevel: NoAccess),
    ];

    /// <summary>Capabilities only Admins have. They are shown as locked rows and can never be granted.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> AdminOnly =
    [
        ("Settings", "Every settings page, test emails and email server checks. They control email delivery and the links in emails, so they stay with Admins."),
        ("Adding and removing staff", "Inviting people, editing staff details and removing someone from the staff."),
        ("Roles", "Making someone an Admin, Manager or Employee."),
        ("Permissions", "Choosing what Managers, Employees and individual people may do."),
        ("Unlocking accounts", "Letting someone sign in again after too many wrong passwords."),
    ];

    /// <summary>What every staff member can always do. These are not permissions and cannot be taken away.</summary>
    public static readonly IReadOnlyList<string> EveryStaffMember =
    [
        "Clock in and out",
        "See their own shifts and hours",
        "Fix their own forgotten clock-out (flagged for review)",
        "View their own profile",
    ];

    /// <summary>Every permission key, in catalog order.</summary>
    public static readonly IReadOnlyList<string> AllPermissions =
        Modules.SelectMany(m => m.Levels).SelectMany(l => l.Grants).Distinct(StringComparer.Ordinal).ToList();

    private static readonly HashSet<string> Known = new(AllPermissions, StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> AdminPermissions = new HashSet<string>(AllPermissions, StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> ManagerDefaults = DefaultsFor(StaffRole.Manager);
    private static readonly IReadOnlySet<string> EmployeeDefaults = DefaultsFor(StaffRole.Employee);

    public static bool IsKnown(string? permission) => permission is not null && Known.Contains(permission);

    public static PermissionModule Module(string key) =>
        Modules.FirstOrDefault(m => m.Key == key) ?? throw new ArgumentException($"Unknown permission module '{key}'.", nameof(key));

    /// <summary>What a role has when no admin has changed its permissions. Admins: everything.</summary>
    public static IReadOnlySet<string> Defaults(StaffRole role) => role switch
    {
        StaffRole.Admin => AdminPermissions,
        StaffRole.Manager => ManagerDefaults,
        StaffRole.Employee => EmployeeDefaults,
        _ => new HashSet<string>(),
    };

    /// <summary>
    /// What a Manager or Employee has: the role's defaults with an admin's choices for that role (permission → granted)
    /// applied on top, normalised. Admins always have everything.
    /// </summary>
    public static IReadOnlySet<string> ForRole(StaffRole role, IReadOnlyDictionary<string, bool>? overrides)
    {
        if (role == StaffRole.Admin || overrides is null || overrides.Count == 0)
        {
            return Defaults(role);
        }

        var granted = new HashSet<string>(Defaults(role), StringComparer.Ordinal);
        foreach (var (permission, isGranted) in overrides)
        {
            if (isGranted)
            {
                granted.Add(permission);
            }
            else
            {
                granted.Remove(permission);
            }
        }

        return Normalize(granted);
    }

    /// <summary>
    /// The modules whose level differs between two permission sets, for people to read, e.g.
    /// "Orders: View → View &amp; change; Inquiries: No access → View". Null when nothing differs.
    /// </summary>
    public static string? DescribeChanges(IReadOnlySet<string> before, IReadOnlySet<string> after)
    {
        var changes = Modules
            .Select(m => (Module: m, Before: LevelFor(m, before), After: LevelFor(m, after)))
            .Where(c => c.Before.Key != c.After.Key)
            .Select(c => $"{c.Module.Name}: {c.Before.Label} → {c.After.Label}")
            .ToList();
        return changes.Count == 0 ? null : string.Join("; ", changes);
    }

    /// <summary>
    /// Drops unknown keys and adds what the remaining keys imply: anything a module level grants pulls in the rest of that
    /// level (X.manage ⇒ X.view, dashboard.sales ⇒ dashboard.view, timeclock.manage ⇒ timeclock.viewall).
    /// </summary>
    public static IReadOnlySet<string> Normalize(IEnumerable<string> permissions)
    {
        var result = new HashSet<string>(permissions.Where(IsKnown), StringComparer.Ordinal);
        foreach (var module in Modules)
        {
            IReadOnlyList<string> lower = [];
            foreach (var level in module.Levels)
            {
                // A key this level adds (not granted by a lower level) means the person has the whole level.
                if (level.Grants.Any(g => !lower.Contains(g) && result.Contains(g)))
                {
                    result.UnionWith(level.Grants);
                }

                lower = level.Grants;
            }
        }

        return result;
    }

    /// <summary>The module level that matches a set of permissions: the highest level whose keys are all granted.</summary>
    public static PermissionLevel LevelFor(PermissionModule module, IReadOnlySet<string> permissions)
    {
        var effective = Normalize(permissions);
        return module.Levels.Last(l => l.Grants.All(effective.Contains));
    }

    public static PermissionLevel LevelFor(string moduleKey, IReadOnlySet<string> permissions) => LevelFor(Module(moduleKey), permissions);

    private static IReadOnlySet<string> DefaultsFor(StaffRole role) =>
        Normalize(Modules.SelectMany(m => m.DefaultLevel(role).Grants));
}
