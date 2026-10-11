using NSubstitute;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Dashboard;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

public sealed class DashboardQueryTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly ICateringInquiryRepository _inquiries = Substitute.For<ICateringInquiryRepository>();
    private readonly IStandingOrderRepository _standingOrders = Substitute.For<IStandingOrderRepository>();

    public DashboardQueryTests()
    {
        var order = new OrderSummaryDto
        {
            OrderNumber = "SV-1001",
            CustomerName = "Rina",
            Status = OrderStatus.Confirmed,
            ScheduledFor = TestData.Now.AddDays(1),
            CreatedAtUtc = TestData.Now,
            Total = 42.50m,
        };

        _orders.GetCreatedSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([order]);
        _orders.GetScheduledBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([order]);
        _orders.CountByStatusAsync(Arg.Any<CancellationToken>()).Returns(new Dictionary<OrderStatus, int> { [OrderStatus.Pending] = 2 });
        _orders.SearchAsync(Arg.Any<OrderSearchCriteria>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<OrderSummaryDto>([order], 1, 1, 8));
        _orders.GetPopularItemsSinceAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<OrderSource?>(), Arg.Any<CancellationToken>())
            .Returns([new PopularItemDto { ItemName = "Aloo Vorta", Unit = "lb", TotalQuantity = 3m, Revenue = 40m }]);
        _inquiries.CountOpenAsync(Arg.Any<CancellationToken>()).Returns(4);
        _standingOrders.CountByStatusAsync(Arg.Any<StandingOrderStatus>(), Arg.Any<CancellationToken>()).Returns(1);
    }

    private Task<DashboardDto> RunAsync(FakeCurrentStaff staff) =>
        new GetDashboardQueryHandler(_orders, _inquiries, _standingOrders, new FakeClock(TestData.Now), staff).Handle(new GetDashboardQuery(), CancellationToken.None);

    [Fact]
    public async Task Admins_SeeEverything_IncludingMoney()
    {
        var d = await RunAsync(FakeCurrentStaff.Admin());

        Assert.True(d.IncludesSales);
        Assert.Equal(42.50m, d.RevenueToday);
        Assert.Equal(42.50m, d.UpcomingOrders.Single().Total);
        Assert.Equal(40m, d.PopularItems.Single().Revenue);
        Assert.Equal(14, d.Last14Days.Count);
        Assert.Equal(4, d.OpenInquiries);
        Assert.Equal(1, d.ActiveStandingOrders);
        Assert.NotNull(d.RestaurantRevenueThisWeek);
    }

    [Fact]
    public async Task EmployeeDefaults_SeeOrderCountsAndLists_ButNoMoneyInquiriesOrRestaurants()
    {
        var d = await RunAsync(FakeCurrentStaff.Defaults(StaffRole.Employee));

        Assert.False(d.IncludesSales);
        Assert.True(d.IncludesOrders);
        Assert.Equal(2, d.PendingOrders);
        Assert.Equal(1, d.OrdersToday);
        Assert.Null(d.RevenueToday);
        Assert.Null(d.RevenueThisMonth);
        Assert.Empty(d.Last14Days);
        Assert.All(d.UpcomingOrders.Concat(d.RecentOrders), o => Assert.Null(o.Total));
        Assert.All(d.PopularItems, i => Assert.Null(i.Revenue));
        Assert.Null(d.OpenInquiries);
        Assert.False(d.IncludesRestaurants);
        Assert.Null(d.StandingOrdersAwaitingApproval);
        Assert.Null(d.RestaurantRevenueThisWeek);
        await _inquiries.DidNotReceive().CountOpenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutOrdersView_NoOrderDataIsLoaded()
    {
        var d = await RunAsync(FakeCurrentStaff.With(StaffRole.Employee, Permissions.DashboardView, Permissions.RestaurantsView));

        Assert.False(d.IncludesOrders);
        Assert.Null(d.PendingOrders);
        Assert.Null(d.OrdersToday);
        Assert.Empty(d.UpcomingOrders);
        Assert.Empty(d.RecentOrders);
        Assert.Empty(d.PopularItems);
        Assert.Equal(1, d.ActiveStandingOrders);
        Assert.Null(d.RestaurantRevenueThisWeek);
        await _orders.DidNotReceive().SearchAsync(Arg.Any<OrderSearchCriteria>(), Arg.Any<CancellationToken>());
        await _orders.DidNotReceive().GetCreatedSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
