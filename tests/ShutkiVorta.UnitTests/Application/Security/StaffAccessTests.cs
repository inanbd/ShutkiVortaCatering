using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.UnitTests.Application.Security;

public sealed class StaffAccessTests
{
    private static readonly IReadOnlyDictionary<StaffRole, IReadOnlyDictionary<string, bool>> NoOverrides =
        new Dictionary<StaffRole, IReadOnlyDictionary<string, bool>>();

    private static StaffAccess Evaluate(
        IReadOnlyCollection<StaffRole> roles,
        bool confirmed = true,
        bool custom = false,
        IEnumerable<string>? grants = null,
        IReadOnlyDictionary<StaffRole, IReadOnlyDictionary<string, bool>>? overrides = null) =>
        StaffAccess.Evaluate("u1", roles, confirmed, custom, grants ?? [], overrides ?? NoOverrides);

    [Fact]
    public void Admins_CanDoEverything()
    {
        var access = Evaluate([StaffRole.Admin]);

        Assert.True(access.IsStaff);
        Assert.True(access.IsAdmin);
        Assert.All(PermissionCatalog.AllPermissions, p => Assert.True(access.Can(p)));
        Assert.Equal("u1", access.UserId);
    }

    [Fact]
    public void RoleDefaults_ApplyWithoutChanges()
    {
        var employee = Evaluate([StaffRole.Employee]);

        Assert.True(employee.IsStaff);
        Assert.False(employee.IsAdmin);
        Assert.True(employee.Can(Permissions.OrdersView));
        Assert.False(employee.Can(Permissions.OrdersManage));
        Assert.False(employee.Can(Permissions.DashboardSales));
        Assert.True(employee.Permissions.SetEquals(PermissionCatalog.Defaults(StaffRole.Employee)));
    }

    [Fact]
    public void SeveralStaffRoles_FailClosedToTheLowest()
    {
        var access = Evaluate([StaffRole.Admin, StaffRole.Employee]);

        Assert.Equal(StaffRole.Employee, access.Role);
        Assert.False(access.IsAdmin);
        Assert.False(access.Can(Permissions.OrdersManage));
        Assert.Equal(StaffRole.Manager, Evaluate([StaffRole.Manager, StaffRole.Admin]).Role);
    }

    [Fact]
    public void UnconfirmedEmail_MeansNoStaffAccess()
    {
        var access = Evaluate([StaffRole.Admin], confirmed: false);

        Assert.Equal(StaffRole.Admin, access.Role);
        Assert.False(access.IsStaff);
        Assert.False(access.IsAdmin);
        Assert.Empty(access.Permissions);
        Assert.False(access.Can(Permissions.DashboardView));
    }

    [Fact]
    public void NoStaffRole_MeansNoStaffAccess()
    {
        var access = Evaluate([], custom: true, grants: [Permissions.OrdersView]);

        Assert.Null(access.Role);
        Assert.False(access.IsStaff);
        Assert.False(access.Can(Permissions.OrdersView));
        Assert.False(StaffAccess.None.IsStaff);
        Assert.Null(StaffAccess.None.UserId);
    }

    [Fact]
    public void CustomPermissions_ReplaceTheRoles_AndAreNormalised()
    {
        var overrides = new Dictionary<StaffRole, IReadOnlyDictionary<string, bool>>
        {
            [StaffRole.Employee] = new Dictionary<string, bool> { [Permissions.CustomersView] = true },
        };

        var access = Evaluate([StaffRole.Employee], custom: true, grants: [Permissions.InquiriesManage, "made.up"], overrides: overrides);

        Assert.True(access.UsesCustomPermissions);
        Assert.Equal(new[] { Permissions.InquiriesManage, Permissions.InquiriesView }.Order(), access.Permissions.Order());
        Assert.False(access.Can(Permissions.DashboardView));
        Assert.False(access.Can(Permissions.CustomersView));
    }

    [Fact]
    public void RoleOverrides_ApplyToThatRoleOnly()
    {
        var overrides = new Dictionary<StaffRole, IReadOnlyDictionary<string, bool>>
        {
            [StaffRole.Employee] = new Dictionary<string, bool> { [Permissions.DashboardView] = false, [Permissions.CustomersView] = true },
        };

        var employee = Evaluate([StaffRole.Employee], overrides: overrides);
        var manager = Evaluate([StaffRole.Manager], overrides: overrides);

        Assert.False(employee.Can(Permissions.DashboardView));
        Assert.True(employee.Can(Permissions.CustomersView));
        Assert.True(manager.Permissions.SetEquals(PermissionCatalog.Defaults(StaffRole.Manager)));
    }

    [Fact]
    public void Admins_IgnoreCustomPermissions()
    {
        var access = Evaluate([StaffRole.Admin], custom: true, grants: [Permissions.OrdersView]);

        Assert.False(access.UsesCustomPermissions);
        Assert.True(access.Can(Permissions.EmailsManage));
    }
}
