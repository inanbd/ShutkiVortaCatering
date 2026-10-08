using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Orders;

public sealed record PlaceOrderCommand : IRequest<PlaceOrderResult>
{
    public string CustomerName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public FulfillmentMethod Fulfillment { get; init; } = FulfillmentMethod.Pickup;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public DateOnly ScheduledDate { get; init; }
    public TimeOnly ScheduledTime { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<CartLineInput> Lines { get; init; } = [];
}

public sealed record PlaceOrderResult(string OrderNumber, string TrackingToken, decimal Total);

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.CustomerName).NotEmpty().WithMessage("Please enter your name.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Please enter your email address.").EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Please enter a phone number.")
            .Must(p => p is not null && p.Count(char.IsDigit) is >= 10 and <= 15)
            .WithMessage("Please enter a valid phone number, e.g. (214) 555-0123.");
        RuleFor(x => x.Fulfillment).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(Order.MaxNotesLength);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Your cart is empty.");
        RuleForEach(x => x.Lines).ChildRules(line => line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(200));

        When(x => x.Fulfillment == FulfillmentMethod.Delivery, () =>
        {
            RuleFor(x => x.AddressLine1).NotEmpty().WithMessage("Please enter your street address.").MaximumLength(200);
            RuleFor(x => x.AddressLine2).MaximumLength(200);
            RuleFor(x => x.City).NotEmpty().WithMessage("Please enter your city.").MaximumLength(100);
            RuleFor(x => x.State).NotEmpty().Length(2).WithMessage("Please use the 2-letter state code, e.g. TX.");
            RuleFor(x => x.PostalCode).NotEmpty().WithMessage("Please enter your ZIP code.")
                .Matches(@"^\d{5}(-\d{4})?$").WithMessage("Please enter a valid ZIP code.");
        });
    }
}

internal sealed class PlaceOrderCommandHandler(
    IMenuItemRepository menu,
    IOrderRepository orders,
    IOrderNumberGenerator numbers,
    OrderSchedule schedule,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<OrderingOptions> options,
    IPublisher publisher) : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<PlaceOrderResult> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (!o.AcceptingOrders)
        {
            throw new ValidationException(o.PausedMessage);
        }

        var scheduleError = schedule.Validate(request.ScheduledDate, request.ScheduledTime);
        if (scheduleError is not null)
        {
            throw new ValidationException(nameof(PlaceOrderCommand.ScheduledDate), scheduleError);
        }

        DeliveryAddress? address = null;
        if (request.Fulfillment == FulfillmentMethod.Delivery)
        {
            if (!o.DeliversTo(request.PostalCode))
            {
                throw new ValidationException(
                    nameof(PlaceOrderCommand.PostalCode),
                    $"Sorry, we don't deliver to {request.PostalCode} yet ({o.DeliveryAreaDescription} only). Please choose pickup instead.");
            }

            address = new DeliveryAddress(
                request.AddressLine1!, request.AddressLine2, request.City!, request.State!.ToUpperInvariant(), request.PostalCode!);
        }

        var ids = request.Lines.Select(l => l.MenuItemId).Distinct().ToArray();
        var items = (await menu.GetByIdsAsync(ids, cancellationToken)).ToDictionary(i => i.Id);
        var lineRequests = new List<OrderLineRequest>();
        foreach (var line in request.Lines)
        {
            if (!items.TryGetValue(line.MenuItemId, out var item))
            {
                throw new ValidationException("An item in your cart is no longer on our menu. Please review your cart.");
            }

            lineRequests.Add(new OrderLineRequest(item, line.Quantity));
        }

        Order order;
        try
        {
            order = Order.Place(
                await GenerateUniqueOrderNumberAsync(cancellationToken),
                numbers.NewTrackingToken(),
                currentUser.UserId,
                new CustomerContact(request.CustomerName, request.Email, request.Phone),
                request.Fulfillment,
                address,
                request.ScheduledDate.ToDateTime(request.ScheduledTime),
                request.Notes,
                lineRequests,
                o.ToPricingPolicy(),
                clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await orders.AddAsync(order, cancellationToken);
        await publisher.Publish(new OrderPlacedNotification(order.OrderNumber), cancellationToken);

        return new PlaceOrderResult(order.OrderNumber, order.TrackingToken, order.Total);
    }

    private async Task<string> GenerateUniqueOrderNumberAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = numbers.NewOrderNumber(clock.BusinessNow);
            if (!await orders.OrderNumberExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique order number.");
    }
}
