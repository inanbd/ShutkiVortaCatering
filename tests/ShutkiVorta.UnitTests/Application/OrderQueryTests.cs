using NSubstitute;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

/// <summary>The customer's private order link can cancel the order, so the order queries only hand it to those who may change orders.</summary>
public sealed class OrderQueryTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly FakeCurrentStaff _staff = new();

    public OrderQueryTests() =>
        _orders.GetByNumberAsync("SV-261008-ABCD", Arg.Any<CancellationToken>()).Returns(_ => TestData.PlaceOrder());

    [Fact]
    public async Task OrderDetails_CarryThePrivateLink_OnlyForStaffWhoMayChangeOrders()
    {
        _staff.Access = StaffAccessFor.Defaults(StaffRole.Employee); // orders: View
        Assert.Null((await Handlers().Handle(new GetOrderDetailsQuery("sv-261008-abcd"), default))!.TrackingToken);

        _staff.Access = StaffAccessFor.Custom(StaffRole.Employee, [Permissions.OrdersView, Permissions.OrdersManage]);
        Assert.Equal("token-123", (await Handlers().Handle(new GetOrderDetailsQuery("SV-261008-ABCD"), default))!.TrackingToken);

        _staff.Access = StaffAccessFor.Admin();
        Assert.Equal("token-123", (await Handlers().Handle(new GetOrderDetailsQuery("SV-261008-ABCD"), default))!.TrackingToken);
    }

    [Fact]
    public async Task TheTrackOrderLookup_ShowsTheOrderReadOnly_WithoutThePrivateLink()
    {
        var found = await Handlers().Handle(new FindGuestOrderQuery("sv-261008-abcd", " rahima@EXAMPLE.com "), default);

        Assert.NotNull(found);
        Assert.Equal("SV-261008-ABCD", found.OrderNumber);
        Assert.Null(found.TrackingToken);
        Assert.False(found.CanCustomerCancel); // pending, so the private link could cancel it: this view cannot

        Assert.Null(await Handlers().Handle(new FindGuestOrderQuery("SV-261008-ABCD", "someone@example.com"), default));
    }

    private OrderQueryHandlers Handlers() =>
        new(_orders, Substitute.For<IIdentityService>(), new FakeCurrentUser(), _staff, new FakeClock(TestData.Now));
}
