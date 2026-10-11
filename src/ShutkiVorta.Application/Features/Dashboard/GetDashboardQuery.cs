using MediatR;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;

namespace ShutkiVorta.Application.Features.Dashboard;

/// <summary>
/// The admin home page. Each section is filled only for staff allowed to see it: order figures and lists need
/// orders.view, restaurant figures restaurants.view, the inquiry count inquiries.view, and every money value
/// dashboard.sales. Hidden data is null or empty, never zero, so a view cannot show "$0.00" for it by mistake.
/// </summary>
[RequirePermission(Permissions.DashboardView)]
public sealed record GetDashboardQuery : IRequest<DashboardDto>;

public sealed record DailySalesDto(DateOnly Date, int Orders, decimal Revenue);

/// <summary>An order on the dashboard; <see cref="Total"/> is null without dashboard.sales.</summary>
public sealed record DashboardOrderDto
{
    public string OrderNumber { get; init; } = string.Empty;
    public OrderStatus Status { get; init; }
    public DateTime ScheduledFor { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string FulfillmentName { get; init; } = string.Empty;
    public string ItemsPreview { get; init; } = string.Empty;
    public bool IsRestaurantOrder { get; init; }
    public string? CompanyName { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public decimal? Total { get; init; }
}

/// <summary>A best seller; <see cref="Revenue"/> is null without dashboard.sales.</summary>
public sealed record DashboardItemDto(string ItemName, string Unit, decimal TotalQuantity, decimal? Revenue);

/// <summary>
/// Sales figures (today, this month, the chart and best sellers) count online orders only: restaurant orders are generated
/// days ahead of delivery, so counting them by creation date would inflate the day they were generated.
/// Restaurant business is reported separately by delivery week.
/// </summary>
public sealed record DashboardDto
{
    /// <summary>dashboard.sales: revenue, the sales chart and order totals are included.</summary>
    public bool IncludesSales { get; init; }

    /// <summary>orders.view: order counts and the upcoming and recent orders are included.</summary>
    public bool IncludesOrders { get; init; }

    /// <summary>restaurants.view: the restaurant figures are included.</summary>
    public bool IncludesRestaurants { get; init; }

    /// <summary>inquiries.view: the open inquiry count is included.</summary>
    public bool IncludesInquiries { get; init; }

    public int? OrdersToday { get; init; }
    public decimal? RevenueToday { get; init; }
    public int? PendingOrders { get; init; }
    public int? ActiveOrders { get; init; }
    public int? OrdersThisMonth { get; init; }
    public decimal? RevenueThisMonth { get; init; }
    public int? OpenInquiries { get; init; }
    public int? StandingOrdersAwaitingApproval { get; init; }
    public int? ActiveStandingOrders { get; init; }
    public int? RestaurantDeliveriesThisWeek { get; init; }
    public decimal? RestaurantRevenueThisWeek { get; init; }
    public DateOnly WeekStart { get; init; }
    public DateOnly WeekEnd { get; init; }
    public IReadOnlyList<DashboardOrderDto> UpcomingOrders { get; init; } = [];
    public IReadOnlyList<DashboardOrderDto> RecentOrders { get; init; } = [];
    public IReadOnlyList<DashboardItemDto> PopularItems { get; init; } = [];

    /// <summary>The sales chart (empty without dashboard.sales).</summary>
    public IReadOnlyList<DailySalesDto> Last14Days { get; init; } = [];
}

internal sealed class GetDashboardQueryHandler(
    IOrderRepository orders,
    ICateringInquiryRepository inquiries,
    IStandingOrderRepository standingOrders,
    IDateTimeProvider clock,
    ICurrentStaff currentStaff) : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var access = await currentStaff.GetAsync(cancellationToken);
        var sales = access.Can(Permissions.DashboardSales);
        var showOrders = access.Can(Permissions.OrdersView);
        var showRestaurants = access.Can(Permissions.RestaurantsView);
        var showInquiries = access.Can(Permissions.InquiriesView);

        var now = clock.BusinessNow;
        var today = DateOnly.FromDateTime(now);
        var monthStartLocal = new DateTime(now.Year, now.Month, 1);

        // Restaurant deliveries scheduled this week (Monday to Sunday), by delivery date rather than creation date.
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekEnd = weekStart.AddDays(6);

        var dashboard = new DashboardDto
        {
            IncludesSales = sales,
            IncludesOrders = showOrders,
            IncludesRestaurants = showRestaurants,
            IncludesInquiries = showInquiries,
            WeekStart = weekStart,
            WeekEnd = weekEnd,
            OpenInquiries = showInquiries ? await inquiries.CountOpenAsync(cancellationToken) : null,
        };

        if (sales || showOrders)
        {
            dashboard = await AddOnlineOrdersAsync(dashboard, now, today, monthStartLocal, cancellationToken);
        }

        if (showOrders)
        {
            var counts = await orders.CountByStatusAsync(cancellationToken);
            int Count(OrderStatus s) => counts.TryGetValue(s, out var c) ? c : 0;

            var upcoming = (await orders.GetScheduledBetweenAsync(now.AddHours(-2), now.AddDays(7), cancellationToken))
                .Where(o => !o.Status.IsFinal())
                .OrderBy(o => o.ScheduledFor)
                .Select(o => ToDashboardOrder(o, sales))
                .ToList();
            var recent = await orders.SearchAsync(new OrderSearchCriteria { Source = OrderSource.Online, Page = 1, PageSize = 8 }, cancellationToken);

            dashboard = dashboard with
            {
                PendingOrders = Count(OrderStatus.Pending),
                ActiveOrders = Count(OrderStatus.Confirmed) + Count(OrderStatus.Preparing) + Count(OrderStatus.ReadyForPickup) + Count(OrderStatus.OutForDelivery),
                UpcomingOrders = upcoming,
                RecentOrders = recent.Items.Select(o => ToDashboardOrder(o, sales)).ToList(),
            };
        }

        if (showRestaurants)
        {
            var restaurantThisWeek = (await orders.GetScheduledBetweenAsync(
                    weekStart.ToDateTime(TimeOnly.MinValue), weekEnd.ToDateTime(TimeOnly.MaxValue), cancellationToken))
                .Where(o => o.IsRestaurantOrder && o.Status != OrderStatus.Cancelled)
                .ToList();

            dashboard = dashboard with
            {
                StandingOrdersAwaitingApproval = await standingOrders.CountByStatusAsync(StandingOrderStatus.PendingApproval, cancellationToken),
                ActiveStandingOrders = await standingOrders.CountByStatusAsync(StandingOrderStatus.Active, cancellationToken),
                RestaurantDeliveriesThisWeek = restaurantThisWeek.Count,
                RestaurantRevenueThisWeek = sales ? restaurantThisWeek.Sum(o => o.Total) : null,
            };
        }

        return dashboard;
    }

    /// <summary>Today's and this month's online orders, the sales chart and the best sellers.</summary>
    private async Task<DashboardDto> AddOnlineOrdersAsync(
        DashboardDto dashboard, DateTime now, DateOnly today, DateTime monthStartLocal, CancellationToken cancellationToken)
    {
        var sales = dashboard.IncludesSales;
        var showOrders = dashboard.IncludesOrders;
        var windowStartLocal = today.AddDays(-13).ToDateTime(TimeOnly.MinValue);
        var sinceUtc = clock.ToUtc(monthStartLocal < windowStartLocal ? monthStartLocal : windowStartLocal);

        var created = (await orders.GetCreatedSinceAsync(sinceUtc, cancellationToken))
            .Select(o => (Order: o, Local: clock.ToBusinessTime(o.CreatedAtUtc)))
            .ToList();
        var billable = created.Where(x => x.Order.Status != OrderStatus.Cancelled && !x.Order.IsRestaurantOrder).ToList();
        var thisMonth = billable.Where(x => x.Local >= monthStartLocal).ToList();
        var todays = billable.Where(x => DateOnly.FromDateTime(x.Local) == today).ToList();

        var last14 = sales
            ? Enumerable.Range(0, 14)
                .Select(i => today.AddDays(-13 + i))
                .Select(d =>
                {
                    var day = billable.Where(x => DateOnly.FromDateTime(x.Local) == d).ToList();
                    return new DailySalesDto(d, day.Count, day.Sum(x => x.Order.Total));
                })
                .ToList()
            : [];

        var popular = await orders.GetPopularItemsSinceAsync(clock.ToUtc(monthStartLocal), 5, OrderSource.Online, cancellationToken);

        return dashboard with
        {
            OrdersToday = showOrders ? todays.Count : null,
            RevenueToday = sales ? todays.Sum(x => x.Order.Total) : null,
            OrdersThisMonth = showOrders ? thisMonth.Count : null,
            RevenueThisMonth = sales ? thisMonth.Sum(x => x.Order.Total) : null,
            Last14Days = last14,
            PopularItems = popular.Select(p => new DashboardItemDto(p.ItemName, p.Unit, p.TotalQuantity, sales ? p.Revenue : null)).ToList(),
        };
    }

    private static DashboardOrderDto ToDashboardOrder(OrderSummaryDto order, bool includeTotal) => new()
    {
        OrderNumber = order.OrderNumber,
        Status = order.Status,
        ScheduledFor = order.ScheduledFor,
        CreatedAtUtc = order.CreatedAtUtc,
        FulfillmentName = order.FulfillmentName,
        ItemsPreview = order.ItemsPreview,
        IsRestaurantOrder = order.IsRestaurantOrder,
        CompanyName = order.CompanyName,
        CustomerName = order.CustomerName,
        Phone = order.Phone,
        Total = includeTotal ? order.Total : null,
    };
}
