using MediatR;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Wholesale;

namespace ShutkiVorta.Application.Features.Wholesale;

/// <summary>Number of standing orders in each status, for the admin list tabs.</summary>
public sealed record GetStandingOrderCountsQuery : IRequest<IReadOnlyDictionary<StandingOrderStatus, int>>, IRequireAdmin;

internal sealed class GetStandingOrderCountsQueryHandler(IStandingOrderRepository standingOrders)
    : IRequestHandler<GetStandingOrderCountsQuery, IReadOnlyDictionary<StandingOrderStatus, int>>
{
    public async Task<IReadOnlyDictionary<StandingOrderStatus, int>> Handle(GetStandingOrderCountsQuery request, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<StandingOrderStatus, int>();
        foreach (var status in Enum.GetValues<StandingOrderStatus>())
        {
            counts[status] = await standingOrders.CountByStatusAsync(status, cancellationToken);
        }

        return counts;
    }
}
