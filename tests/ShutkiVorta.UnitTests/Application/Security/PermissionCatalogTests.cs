using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.UnitTests.Application.Security;

public sealed class PermissionCatalogTests
{
    /// <summary>
    /// Pins the defaults table: changing what Managers or Employees get by default changes what every existing person may do
    /// on the next deploy, so it must be a deliberate, reviewed change here too.
    /// </summary>
    [Fact]
    public void Defaults_MatchTheAgreedTable()
    {
        var expected = new Dictionary<string, (string Manager, string Employee)>
        {
            ["dashboard"] = ("With sales", "Counts"),
            ["orders"] = ("View & change", "View"),
            ["restaurants"] = ("View & change", "No access"),
            ["production"] = ("View", "View"),
            ["menu"] = ("View & change", "View"),
            ["inventory"] = ("View & change", "View"),
            ["inquiries"] = ("View & change", "No access"),
            ["customers"] = ("View", "No access"),
            ["timeclock"] = ("See & fix team", "Own shifts only"),
            ["staff"] = ("View the staff list", "No access"),
            ["emails"] = ("View", "No access"),
        };

        Assert.Equal(expected.Keys, PermissionCatalog.Modules.Select(m => m.Key));
        foreach (var module in PermissionCatalog.Modules)
        {
            Assert.Equal(expected[module.Key].Manager, PermissionCatalog.LevelFor(module, PermissionCatalog.Defaults(StaffRole.Manager)).Label);
            Assert.Equal(expected[module.Key].Employee, PermissionCatalog.LevelFor(module, PermissionCatalog.Defaults(StaffRole.Employee)).Label);
        }

        string[] manager =
        [
            Permissions.DashboardView, Permissions.DashboardSales, Permissions.OrdersView, Permissions.OrdersManage,
            Permissions.RestaurantsView, Permissions.RestaurantsManage, Permissions.ProductionView, Permissions.MenuView,
            Permissions.MenuManage, Permissions.InventoryView, Permissions.InventoryManage, Permissions.InquiriesView,
            Permissions.InquiriesManage, Permissions.CustomersView, Permissions.TimeClockViewAll, Permissions.TimeClockManage,
            Permissions.StaffView, Permissions.EmailsView,
        ];
        string[] employee = [Permissions.DashboardView, Permissions.OrdersView, Permissions.ProductionView, Permissions.MenuView, Permissions.InventoryView];
        Assert.Equal(manager.Order(), PermissionCatalog.Defaults(StaffRole.Manager).Order());
        Assert.Equal(employee.Order(), PermissionCatalog.Defaults(StaffRole.Employee).Order());
        Assert.True(PermissionCatalog.Defaults(StaffRole.Admin).SetEquals(PermissionCatalog.AllPermissions));
    }

    [Fact]
    public void Catalog_KnowsEveryPermissionConstant_AndNothingElse()
    {
        var constants = typeof(Permissions).GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order()
            .ToList();

        Assert.Equal(19, constants.Count);
        Assert.Equal(constants, PermissionCatalog.AllPermissions.Order());
        Assert.All(constants, c => Assert.True(PermissionCatalog.IsKnown(c)));
        Assert.False(PermissionCatalog.IsKnown("settings.manage"));
        Assert.False(PermissionCatalog.IsKnown(null));
    }

    [Fact]
    public void Levels_AreCumulative()
    {
        foreach (var module in PermissionCatalog.Modules)
        {
            Assert.Empty(module.Levels[0].Grants);
            for (var i = 1; i < module.Levels.Count; i++)
            {
                Assert.True(module.Levels[i - 1].Grants.All(module.Levels[i].Grants.Contains), $"{module.Key} level {i} must include level {i - 1}");
            }
        }
    }

    [Theory]
    [InlineData(Permissions.OrdersManage, Permissions.OrdersView)]
    [InlineData(Permissions.RestaurantsManage, Permissions.RestaurantsView)]
    [InlineData(Permissions.MenuManage, Permissions.MenuView)]
    [InlineData(Permissions.InventoryManage, Permissions.InventoryView)]
    [InlineData(Permissions.InquiriesManage, Permissions.InquiriesView)]
    [InlineData(Permissions.EmailsManage, Permissions.EmailsView)]
    [InlineData(Permissions.DashboardSales, Permissions.DashboardView)]
    [InlineData(Permissions.TimeClockManage, Permissions.TimeClockViewAll)]
    public void Normalize_AddsWhatAPermissionImplies(string granted, string implied)
    {
        var result = PermissionCatalog.Normalize([granted]);

        Assert.Equal([implied, granted], result.OrderBy(p => p == granted));
    }

    [Fact]
    public void Normalize_DropsUnknownKeys_AndNeverAddsHigherLevels()
    {
        var result = PermissionCatalog.Normalize(["settings.manage", "ORDERS.VIEW", Permissions.OrdersView, Permissions.DashboardView, ""]);

        Assert.Equal(new[] { Permissions.DashboardView, Permissions.OrdersView }.Order(), result.Order());
    }

    [Fact]
    public void LevelFor_PicksTheHighestLevelFullyGranted()
    {
        var orders = PermissionCatalog.Module("orders");
        Assert.Equal(PermissionCatalog.NoAccess, PermissionCatalog.LevelFor(orders, new HashSet<string>()).Key);
        Assert.Equal("view", PermissionCatalog.LevelFor(orders, new HashSet<string> { Permissions.OrdersView }).Key);
        Assert.Equal("manage", PermissionCatalog.LevelFor(orders, new HashSet<string> { Permissions.OrdersView, Permissions.OrdersManage }).Key);

        // A stored manage key without its view key still reads as the manage level (implied view).
        Assert.Equal("manage", PermissionCatalog.LevelFor("orders", new HashSet<string> { Permissions.OrdersManage }).Key);
        Assert.Equal("own", PermissionCatalog.LevelFor("timeclock", new HashSet<string>()).Key);
        Assert.Equal("sales", PermissionCatalog.LevelFor("dashboard", new HashSet<string> { Permissions.DashboardSales }).Key);
    }

    [Fact]
    public void ForRole_AppliesAnAdminsChangesOnTopOfTheDefaults()
    {
        var overrides = new Dictionary<string, bool>
        {
            [Permissions.DashboardView] = false,
            [Permissions.CustomersView] = true,
            [Permissions.MenuManage] = true,
        };

        var result = PermissionCatalog.ForRole(StaffRole.Employee, overrides);

        string[] expected = [Permissions.CustomersView, Permissions.InventoryView, Permissions.MenuManage, Permissions.MenuView, Permissions.OrdersView, Permissions.ProductionView];
        Assert.Equal(expected.Order(), result.Order());
        Assert.Same(PermissionCatalog.Defaults(StaffRole.Manager), PermissionCatalog.ForRole(StaffRole.Manager, null));
    }

    [Fact]
    public void DescribeChanges_NamesModulesAndLevels()
    {
        var before = PermissionCatalog.Defaults(StaffRole.Employee);
        var after = PermissionCatalog.Normalize([.. before, Permissions.OrdersManage, Permissions.InquiriesView]);

        Assert.Equal("Orders: View → View & change; Inquiries: No access → View", PermissionCatalog.DescribeChanges(before, after));
        Assert.Null(PermissionCatalog.DescribeChanges(before, before));
    }

    [Fact]
    public void AdminOnlyCapabilities_IncludeSettingsAndStaffManagement()
    {
        Assert.Contains(PermissionCatalog.AdminOnly, c => c.Name == "Settings");
        Assert.Contains(PermissionCatalog.AdminOnly, c => c.Name == "Roles");
        Assert.Contains(PermissionCatalog.AdminOnly, c => c.Name == "Permissions");
        Assert.DoesNotContain(PermissionCatalog.AllPermissions, p => p.StartsWith("settings.", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownPermissionsCannotBeRequired()
    {
        Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute("settings.manage"));
    }
}
