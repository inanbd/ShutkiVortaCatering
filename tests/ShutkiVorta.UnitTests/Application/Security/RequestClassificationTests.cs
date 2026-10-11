using System.Reflection;
using MediatR;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Dashboard;

namespace ShutkiVorta.UnitTests.Application.Security;

/// <summary>
/// Every request states who may send it, so a new request cannot be left open by accident: exactly one of IRequireAdmin,
/// IRequireStaff or [RequirePermission], or IRequireAuthenticatedUser for customers, or an explicit entry in the public list.
/// </summary>
public sealed class RequestClassificationTests
{
    /// <summary>Requests anyone may send: the public site, checkout, sign-in and account recovery.</summary>
    private static readonly HashSet<string> Public =
    [
        "GetSitemapQuery", "GetRobotsTxtQuery",
        "GetMenuQuery", "GetMenuItemBySlugQuery", "GetWholesaleCatalogQuery",
        "PriceCartQuery", "GetCheckoutOptionsQuery", "PlaceOrderCommand",
        "GetOrderByTrackingTokenQuery", "FindGuestOrderQuery", "CancelOrderByCustomerCommand",
        "SubmitCateringInquiryCommand",
        "RegisterCustomerCommand", "LoginCommand", "LogoutCommand", "ConfirmEmailCommand", "ResendConfirmationEmailCommand",
        "ForgotPasswordCommand", "ResetPasswordCommand",
    ];

    /// <summary>The agreed permission of every admin request that existed before staff roles (the design's table).</summary>
    private static readonly Dictionary<string, string> ExpectedPermissions = new()
    {
        ["GetDashboardQuery"] = Permissions.DashboardView,
        ["GetOrdersQuery"] = Permissions.OrdersView,
        ["GetOrderDetailsQuery"] = Permissions.OrdersView,
        ["UpdateOrderStatusCommand"] = Permissions.OrdersManage,
        ["SaveOrderAdminNotesCommand"] = Permissions.OrdersManage,
        ["GetStandingOrderCountsQuery"] = Permissions.RestaurantsView,
        ["GetStandingOrdersQuery"] = Permissions.RestaurantsView,
        ["GetStandingOrderQuery"] = Permissions.RestaurantsView,
        ["GetStandingOrderStatementQuery"] = Permissions.RestaurantsView,
        ["ChangeStandingOrderStatusCommand"] = Permissions.RestaurantsManage,
        ["SkipStandingOrderDateCommand"] = Permissions.RestaurantsManage,
        ["UpdateStandingOrderTermsCommand"] = Permissions.RestaurantsManage,
        ["GenerateStandingOrderDeliveriesCommand"] = Permissions.RestaurantsManage,
        ["GetProductionPlanQuery"] = Permissions.ProductionView,
        ["GetAdminMenuQuery"] = Permissions.MenuView,
        ["GetMenuItemByIdQuery"] = Permissions.MenuView,
        ["SetMenuItemAvailabilityCommand"] = Permissions.MenuManage,
        ["DeleteMenuItemCommand"] = Permissions.MenuManage,
        ["UploadMenuImageCommand"] = Permissions.MenuManage,
        ["SaveMenuItemCommand"] = Permissions.MenuManage,
        ["GetInventoryOverviewQuery"] = Permissions.InventoryView,
        ["GetInventoryPurchaseQuery"] = Permissions.InventoryView,
        ["GetInventoryFormOptionsQuery"] = Permissions.InventoryView,
        ["GetInventoryItemsQuery"] = Permissions.InventoryView,
        ["GetInventoryItemQuery"] = Permissions.InventoryView,
        ["GetInventoryReceiptFileQuery"] = Permissions.InventoryView,
        ["SaveInventoryPurchaseCommand"] = Permissions.InventoryManage,
        ["DeleteInventoryPurchaseCommand"] = Permissions.InventoryManage,
        ["SaveInventoryItemCommand"] = Permissions.InventoryManage,
        ["DeleteInventoryItemCommand"] = Permissions.InventoryManage,
        ["GetInquiriesQuery"] = Permissions.InquiriesView,
        ["SetInquiryHandledCommand"] = Permissions.InquiriesManage,
        ["GetEmailLogQuery"] = Permissions.EmailsView,
        ["GetEmailLogEntryQuery"] = Permissions.EmailsView,
        ["RetryEmailCommand"] = Permissions.EmailsManage,
        ["RetryFailedEmailsCommand"] = Permissions.EmailsManage,
    };

    /// <summary>Admin only: settings (they control email delivery and links) and anything that changes staff access.</summary>
    private static readonly HashSet<string> ExpectedAdminOnly =
    [
        "TestSmtpConnectionCommand", "GetSettingsOverviewQuery", "SendTestEmailCommand", "GetSettingsPagesQuery",
        "GetSettingsNoticesQuery", "GetSettingsPageQuery", "UpdateSettingsPageCommand",
        "GetUsersQuery", // the old user list; removed with the new customer list
    ];

    private static readonly IReadOnlyList<Type> Requests = typeof(GetDashboardQuery).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
        .ToList();

    public static TheoryData<string> RequestNames()
    {
        var data = new TheoryData<string>();
        foreach (var type in Requests)
        {
            data.Add(type.FullName!);
        }

        return data;
    }

    [Fact]
    public void TheListsNameRealRequests()
    {
        Assert.True(Requests.Count > 60);
        Assert.All(Public.Concat(ExpectedPermissions.Keys).Concat(ExpectedAdminOnly), name => Assert.Single(Requests, t => t.Name == name));
    }

    [Theory]
    [MemberData(nameof(RequestNames))]
    public void EveryRequest_IsClassifiedExactlyOnce(string fullName)
    {
        var type = Requests.Single(t => t.FullName == fullName);
        var staffMarkers = Markers(type);
        var customer = typeof(IRequireAuthenticatedUser).IsAssignableFrom(type);

        if (Public.Contains(type.Name))
        {
            Assert.True(staffMarkers.Count == 0 && !customer, $"{type.Name} is listed as public but also carries an authorization marker.");
            return;
        }

        Assert.True(staffMarkers.Count + (customer ? 1 : 0) == 1,
            $"{type.Name} must carry exactly one of IRequireAdmin, IRequireStaff, [RequirePermission] or IRequireAuthenticatedUser " +
            $"(found: {string.Join(", ", staffMarkers.Append(customer ? "IRequireAuthenticatedUser" : null).OfType<string>())}), or be added to the public list.");
    }

    [Fact]
    public void ExistingAdminRequests_HaveTheirAgreedPermission()
    {
        foreach (var (name, permission) in ExpectedPermissions)
        {
            var type = Requests.Single(t => t.Name == name);
            Assert.Equal(permission, type.GetCustomAttribute<RequirePermissionAttribute>()?.Permission);
            Assert.False(typeof(IRequireAdmin).IsAssignableFrom(type), $"{name} must not also require an Admin.");
        }

        foreach (var name in ExpectedAdminOnly)
        {
            Assert.True(typeof(IRequireAdmin).IsAssignableFrom(Requests.Single(t => t.Name == name)), $"{name} must be for Admins only.");
        }
    }

    /// <summary>Changing who is staff, and what they may do, is for Admins only; staff.view may only read the staff list.</summary>
    [Fact]
    public void StaffAndUserRequests_AreForAdmins_ExceptStaffListQueries()
    {
        var staffRequests = Requests.Where(t => t.Namespace is "ShutkiVorta.Application.Features.Staff" or "ShutkiVorta.Application.Features.Users");
        foreach (var type in staffRequests)
        {
            var admin = typeof(IRequireAdmin).IsAssignableFrom(type);
            var staffViewQuery = type.Name.EndsWith("Query", StringComparison.Ordinal)
                && type.GetCustomAttribute<RequirePermissionAttribute>()?.Permission == Permissions.StaffView;
            Assert.True(admin || staffViewQuery, $"{type.Name} must be IRequireAdmin (or a [RequirePermission(staff.view)] query).");
        }
    }

    private static List<string> Markers(Type type)
    {
        var markers = new List<string>();
        if (typeof(IRequireAdmin).IsAssignableFrom(type))
        {
            markers.Add("IRequireAdmin");
        }

        if (typeof(IRequireStaff).IsAssignableFrom(type))
        {
            markers.Add("IRequireStaff");
        }

        if (type.GetCustomAttribute<RequirePermissionAttribute>() is not null)
        {
            markers.Add("[RequirePermission]");
        }

        return markers;
    }
}
