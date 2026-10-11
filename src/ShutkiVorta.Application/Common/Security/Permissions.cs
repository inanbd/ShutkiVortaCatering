namespace ShutkiVorta.Application.Common.Security;

/// <summary>
/// Permission keys stored in the database (RolePermissions, UserPermissions). Admins always have every permission;
/// what Managers and Employees have is chosen on Admin → Permissions (see <see cref="PermissionCatalog"/>).
/// </summary>
public static class Permissions
{
    /// <summary>The dashboard with order counts (and, with <see cref="OrdersView"/>, the upcoming and recent orders).</summary>
    public const string DashboardView = "dashboard.view";

    /// <summary>Revenue cards, the sales chart, best-seller revenue and order totals on the dashboard.</summary>
    public const string DashboardSales = "dashboard.sales";

    public const string OrdersView = "orders.view";

    /// <summary>Change an order's status (can email the customer), internal notes, and the customer's tracking link.</summary>
    public const string OrdersManage = "orders.manage";

    public const string RestaurantsView = "restaurants.view";

    /// <summary>Approve, decline, pause, resume or cancel; skip dates; edit terms and prices; generate deliveries.</summary>
    public const string RestaurantsManage = "restaurants.manage";

    public const string ProductionView = "production.view";

    public const string MenuView = "menu.view";

    /// <summary>Add, edit, hide and delete menu items and upload their photos.</summary>
    public const string MenuManage = "menu.manage";

    public const string InventoryView = "inventory.view";

    /// <summary>Add, edit and delete purchases and receipt photos; manage saved item names.</summary>
    public const string InventoryManage = "inventory.manage";

    public const string InquiriesView = "inquiries.view";

    /// <summary>Mark inquiries handled or re-open them.</summary>
    public const string InquiriesManage = "inquiries.manage";

    public const string CustomersView = "customers.view";

    /// <summary>Everyone's shifts, locations, hours and exports. (Every staff member always sees their own.)</summary>
    public const string TimeClockViewAll = "timeclock.viewall";

    /// <summary>Add, correct and void OTHER people's shifts (only Admins may fix their own).</summary>
    public const string TimeClockManage = "timeclock.manage";

    /// <summary>The read-only staff list. Changing anyone's access is for Admins only.</summary>
    public const string StaffView = "staff.view";

    public const string EmailsView = "emails.view";

    /// <summary>Send emails again.</summary>
    public const string EmailsManage = "emails.manage";
}
