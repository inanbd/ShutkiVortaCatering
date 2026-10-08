using MediatR;
using Microsoft.Extensions.Options;
using NSubstitute;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

public sealed class PlaceOrderCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 15, 0, 0);

    private readonly IMenuItemRepository _menu = Substitute.For<IMenuItemRepository>();
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderNumberGenerator _numbers = Substitute.For<IOrderNumberGenerator>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly FakeCurrentUser _user = new();
    private readonly OrderingOptions _options = new()
    {
        MinimumLeadTimeHours = 24,
        MaxDaysInAdvance = 30,
        ClosedDays = [],
        DeliveryZipPrefixes = ["752"],
    };

    public PlaceOrderCommandHandlerTests()
    {
        _menu.GetByIdsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns([TestData.Item(1, "Loitta Shutki Vorta", 24.99m), TestData.Item(2, "Aloo Vorta", 11.99m)]);
        _numbers.NewOrderNumber(Arg.Any<DateTime>()).Returns("SV-261008-TEST");
        _numbers.NewTrackingToken().Returns("tracking-token");
    }

    private PlaceOrderCommandHandler CreateHandler()
    {
        var clock = new FakeClock(Now);
        var options = Options.Create(_options);
        return new PlaceOrderCommandHandler(_menu, _orders, _numbers, new OrderSchedule(options, clock), _user, clock, options, _publisher);
    }

    private static PlaceOrderCommand Command(FulfillmentMethod fulfillment = FulfillmentMethod.Pickup, string zip = "75201") => new()
    {
        CustomerName = "Rahima Akter",
        Email = "rahima@example.com",
        Phone = "214-555-0199",
        Fulfillment = fulfillment,
        AddressLine1 = "1500 Marilla St",
        City = "Dallas",
        State = "tx",
        PostalCode = zip,
        ScheduledDate = new DateOnly(2026, 10, 10),
        ScheduledTime = new TimeOnly(12, 0),
        Lines = [new CartLineInput(1, 1.5m), new CartLineInput(2, 2m)],
    };

    [Fact]
    public async Task Handle_SavesOrder_AndPublishesNotification()
    {
        _user.UserId = "user-1";

        var result = await CreateHandler().Handle(Command(), CancellationToken.None);

        Assert.Equal("SV-261008-TEST", result.OrderNumber);
        Assert.Equal("tracking-token", result.TrackingToken);
        await _orders.Received(1).AddAsync(
            Arg.Is<Order>(o => o.CustomerId == "user-1" && o.Lines.Count == 2 && o.Subtotal == 61.47m && o.ScheduledFor == new DateTime(2026, 10, 10, 12, 0, 0)),
            Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(Arg.Is<OrderPlacedNotification>(n => n.OrderNumber == "SV-261008-TEST"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RetriesWhenOrderNumberAlreadyExists()
    {
        _orders.OrderNumberExistsAsync("SV-261008-TEST", Arg.Any<CancellationToken>()).Returns(true, false);

        await CreateHandler().Handle(Command(), CancellationToken.None);

        await _orders.Received(2).OrderNumberExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RejectsSlotInsideLeadTime()
    {
        var command = Command() with { ScheduledDate = new DateOnly(2026, 10, 8) };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(command, CancellationToken.None));
        Assert.Contains(nameof(PlaceOrderCommand.ScheduledDate), ex.Errors.Keys);
        await _orders.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_RejectsDeliveryOutsideServiceArea()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateHandler().Handle(Command(FulfillmentMethod.Delivery, zip: "10001"), CancellationToken.None));

        Assert.Contains(nameof(PlaceOrderCommand.PostalCode), ex.Errors.Keys);
    }

    [Fact]
    public async Task Handle_DeliveryUppercasesState()
    {
        await CreateHandler().Handle(Command(FulfillmentMethod.Delivery), CancellationToken.None);

        await _orders.Received(1).AddAsync(Arg.Is<Order>(o => o.State == "TX" && o.DeliveryFee == 10m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RejectsWhenOrdersArePaused()
    {
        _options.AcceptingOrders = false;
        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(Command(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_TurnsDomainRuleViolationsIntoValidationErrors()
    {
        var command = Command() with { Lines = [new CartLineInput(1, 0.3m)] };
        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().Handle(command, CancellationToken.None));
    }

    [Fact]
    public void Validator_RequiresAddressForDelivery()
    {
        var command = Command(FulfillmentMethod.Delivery) with { AddressLine1 = null, PostalCode = "abc" };
        var result = new PlaceOrderCommandValidator().Validate(command);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.AddressLine1));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.PostalCode));
    }

    [Fact]
    public void Validator_RejectsBadContactDetails()
    {
        var command = Command() with { Email = "not-an-email", Phone = "123", Lines = [] };
        var result = new PlaceOrderCommandValidator().Validate(command);

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.Email));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.Phone));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.Lines));
    }
}
