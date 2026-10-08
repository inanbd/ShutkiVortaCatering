using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Features.Orders;

public sealed record GetCheckoutOptionsQuery : IRequest<CheckoutOptionsDto>;

public sealed record CheckoutOptionsDto
{
    public bool AcceptingOrders { get; init; }
    public required string PausedMessage { get; init; }
    public IReadOnlyList<ScheduleDay> Days { get; init; } = [];
    public int MinimumLeadTimeHours { get; init; }
    public decimal DeliveryFee { get; init; }
    public decimal? FreeDeliveryThreshold { get; init; }
    public decimal MinimumDeliverySubtotal { get; init; }
    public decimal TaxRate { get; init; }
    public required string DeliveryAreaDescription { get; init; }
    public required string PaymentInstructions { get; init; }
    public required string PickupInstructions { get; init; }
    public required string PickupLocation { get; init; }
}

internal sealed class GetCheckoutOptionsQueryHandler(
    OrderSchedule schedule,
    IOptions<OrderingOptions> ordering,
    IOptions<BusinessOptions> business) : IRequestHandler<GetCheckoutOptionsQuery, CheckoutOptionsDto>
{
    public Task<CheckoutOptionsDto> Handle(GetCheckoutOptionsQuery request, CancellationToken cancellationToken)
    {
        var o = ordering.Value;
        var b = business.Value;
        return Task.FromResult(new CheckoutOptionsDto
        {
            AcceptingOrders = o.AcceptingOrders,
            PausedMessage = o.PausedMessage,
            Days = schedule.GetAvailableDays(),
            MinimumLeadTimeHours = o.MinimumLeadTimeHours,
            DeliveryFee = o.DeliveryFee,
            FreeDeliveryThreshold = o.FreeDeliveryThreshold,
            MinimumDeliverySubtotal = o.MinimumDeliverySubtotal,
            TaxRate = o.TaxRate,
            DeliveryAreaDescription = o.DeliveryAreaDescription,
            PaymentInstructions = o.PaymentInstructions,
            PickupInstructions = b.PickupInstructions,
            PickupLocation = b.FullAddress,
        });
    }
}
