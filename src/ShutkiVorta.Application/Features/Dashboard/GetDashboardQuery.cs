using MediatR;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Inquiries;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Dashboard;

public sealed record GetDashboardQuery : IRequest<DashboardDto>, IRequireAdmin;

public sealed record DailySalesDto(DateOnly Date, int Orders, decimal Revenue);

public sealed record DashboardDto
{
    public int OrdersToday { get; init; }
    public decimal RevenueToday { get; init; }
    public int PendingOrders { get; init; }
    public int ActiveOrders { get; init; }
    public int OrdersThisMonth { get; init; }
    public decimal RevenueThisMonth { get; init; }
    public int OpenInquiries { get; init; }
    public IReadOnlyList<OrderSummaryDto> UpcomingOrders { get; init; } = [];
    public IReadOnlyList<OrderSummaryDto> RecentOrders { get; init; } = [];
    public IReadOnlyList<PopularItemDto> PopularItems { get; init; } = [];
    public IReadOnlyList<DailySalesDto> Last14Days { get; init; } = [];
}

internal sealed class GetDashboardQueryHandler(
    IOrderRepository orders,
    ICateringInquiryRepository inquiries,
    IDateTimeProvider clock) : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    public async Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        var now = clock.BusinessNow;
        var today = DateOnly.FromDateTime(now);
        var monthStartLocal = new DateTime(now.Year, now.Month, 1);
        var windowStartLocal = today.AddDays(-13).ToDateTime(TimeOnly.MinValue);
        var sinceUtc = clock.ToUtc(monthStartLocal < windowStartLocal ? monthStartLocal : windowStartLocal);

        var created = (await orders.GetCreatedSinceAsync(sinceUtc, cancellationToken))
            .Select(o => (Order: o, Local: clock.ToBusinessTime(o.CreatedAtUtc)))
            .ToList();
        var billable = created.Where(x => x.Order.Status != OrderStatus.Cancelled).ToList();

        var counts = await orders.CountByStatusAsync(cancellationToken);
        int Count(OrderStatus s) => counts.TryGetValue(s, out var c) ? c : 0;

        var upcoming = (await orders.GetScheduledBetweenAsync(now.AddHours(-2), now.AddDays(7), cancellationToken))
            .Where(o => !o.Status.IsFinal())
            .OrderBy(o => o.ScheduledFor)
            .ToList();

        var recent = await orders.SearchAsync(new OrderSearchCriteria { Page = 1, PageSize = 8 }, cancellationToken);

        var last14 = Enumerable.Range(0, 14)
            .Select(i => today.AddDays(-13 + i))
            .Select(d =>
            {
                var day = billable.Where(x => DateOnly.FromDateTime(x.Local) == d).ToList();
                return new DailySalesDto(d, day.Count, day.Sum(x => x.Order.Total));
            })
            .ToList();

        var thisMonth = billable.Where(x => x.Local >= monthStartLocal).ToList();
        var todays = billable.Where(x => DateOnly.FromDateTime(x.Local) == today).ToList();

        return new DashboardDto
        {
            OrdersToday = todays.Count,
            RevenueToday = todays.Sum(x => x.Order.Total),
            PendingOrders = Count(OrderStatus.Pending),
            ActiveOrders = Count(OrderStatus.Confirmed) + Count(OrderStatus.Preparing) + Count(OrderStatus.ReadyForPickup) + Count(OrderStatus.OutForDelivery),
            OrdersThisMonth = thisMonth.Count,
            RevenueThisMonth = thisMonth.Sum(x => x.Order.Total),
            OpenInquiries = await inquiries.CountOpenAsync(cancellationToken),
            UpcomingOrders = upcoming,
            RecentOrders = recent.Items,
            PopularItems = await orders.GetPopularItemsSinceAsync(clock.ToUtc(monthStartLocal), 5, cancellationToken),
            Last14Days = last14,
        };
    }
}
