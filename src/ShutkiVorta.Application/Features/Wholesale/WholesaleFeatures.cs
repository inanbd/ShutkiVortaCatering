using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Menu;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Domain.Wholesale;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.Application.Features.Wholesale;

// ======================================================================================================
// Requests
// ======================================================================================================

/// <summary>Public wholesale price list and ordering rules for restaurants.</summary>
public sealed record GetWholesaleCatalogQuery : IRequest<WholesaleCatalogDto>;

public sealed record StandingOrderItemInput(int MenuItemId, decimal Quantity);

/// <summary>A restaurant asks for recurring deliveries. Returns the new standing order reference.</summary>
public sealed record SubmitStandingOrderCommand : IRequest<string>, IRequireAuthenticatedUser
{
    public string BusinessName { get; init; } = string.Empty;
    public string ContactName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? TaxPermitNumber { get; init; }
    public FulfillmentMethod Fulfillment { get; init; } = FulfillmentMethod.Delivery;
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];
    public TimeOnly PreferredTime { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<StandingOrderItemInput> Items { get; init; } = [];
}

public enum StandingOrderAction
{
    Approve,
    Decline,
    Pause,
    Resume,
    Cancel,
}

/// <summary>Admin changes the status of a standing order.</summary>
public sealed record ChangeStandingOrderStatusCommand(int Id, StandingOrderAction Action, string? Note, bool NotifyRestaurant = true)
    : IRequest<string>, IRequireAdmin;

/// <summary>A restaurant pauses, resumes or cancels its own standing order.</summary>
public sealed record ChangeMyStandingOrderCommand(string Reference, StandingOrderAction Action, string? Reason)
    : IRequest<string>, IRequireAuthenticatedUser;

public sealed record SkipStandingOrderDateCommand(int Id, DateOnly Date, bool Skip) : IRequest, IRequireAdmin;

public sealed record SkipMyStandingOrderDateCommand(string Reference, DateOnly Date, bool Skip) : IRequest, IRequireAuthenticatedUser;

public sealed record StandingOrderTermsLine(int MenuItemId, decimal Quantity, decimal UnitPrice);

/// <summary>Admin edits the agreement (items, agreed prices, schedule, fee, tax status, notes).</summary>
public sealed record UpdateStandingOrderTermsCommand : IRequest, IRequireAdmin
{
    public int Id { get; init; }
    public IReadOnlyList<StandingOrderTermsLine> Lines { get; init; } = [];
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];
    public TimeOnly PreferredTime { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public decimal DeliveryFee { get; init; }
    public bool TaxExempt { get; init; }
    public string? AdminNotes { get; init; }
}

public sealed record GenerateStandingOrderDeliveriesCommand : IRequest<GenerationReport>, IRequireAdmin;

public sealed record GetStandingOrdersQuery(StandingOrderStatus? Status, string? Search, int Page = 1, int PageSize = Paging.DefaultPageSize)
    : IRequest<PagedResult<StandingOrderSummaryDto>>, IRequireAdmin;

public sealed record GetStandingOrderQuery(int Id) : IRequest<StandingOrderDetailsDto?>, IRequireAdmin;

public sealed record GetMyStandingOrdersQuery : IRequest<IReadOnlyList<StandingOrderSummaryDto>>, IRequireAuthenticatedUser;

public sealed record GetMyStandingOrderQuery(string Reference) : IRequest<StandingOrderDetailsDto?>, IRequireAuthenticatedUser;

public sealed record GetStandingOrderStatementQuery(int Id, DateOnly From, DateOnly To) : IRequest<StatementDto?>, IRequireAdmin;

public sealed record GetProductionPlanQuery(DateOnly? From, int Days = 7) : IRequest<ProductionPlanDto>, IRequireAdmin;

public sealed record StandingOrderSubmittedNotification(int StandingOrderId) : INotification;

public sealed record StandingOrderDateChangedNotification(int StandingOrderId, DateOnly Date, bool Skipped) : INotification;

public sealed record StandingOrderStatusChangedNotification(int StandingOrderId, StandingOrderAction Action, string? Note, bool ByRestaurant, bool NotifyRestaurant)
    : INotification;

// ======================================================================================================
// Validation
// ======================================================================================================

public sealed class SubmitStandingOrderCommandValidator : AbstractValidator<SubmitStandingOrderCommand>
{
    public SubmitStandingOrderCommandValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().WithMessage("Please enter your restaurant's name.").MaximumLength(150);
        RuleFor(x => x.ContactName).NotEmpty().WithMessage("Please enter a contact name.").MaximumLength(120);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).NotEmpty().Must(p => p is not null && p.Count(char.IsDigit) is >= 10 and <= 15)
            .WithMessage("Please enter a valid phone number.");
        RuleFor(x => x.TaxPermitNumber).MaximumLength(32);
        RuleFor(x => x.Fulfillment).IsInEnum();
        RuleFor(x => x.Days).NotEmpty().WithMessage("Please choose at least one delivery day.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Please add at least one vorta.");
        RuleFor(x => x.Notes).MaximumLength(StandingOrder.MaxNotesLength);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate.HasValue)
            .WithMessage("The end date must be on or after the start date.");

        When(x => x.Fulfillment == FulfillmentMethod.Delivery, () =>
        {
            RuleFor(x => x.AddressLine1).NotEmpty().WithMessage("Please enter the delivery address.").MaximumLength(200);
            RuleFor(x => x.City).NotEmpty().WithMessage("Please enter the city.").MaximumLength(100);
            RuleFor(x => x.State).NotEmpty().Length(2).WithMessage("Please use the 2-letter state code, e.g. TX.");
            RuleFor(x => x.PostalCode).NotEmpty().Matches(@"^\d{5}(-\d{4})?$").WithMessage("Please enter a valid ZIP code.");
        });
    }
}

// ======================================================================================================
// Handlers
// ======================================================================================================

internal sealed class WholesaleQueryHandlers(
    IMenuItemRepository menu,
    IStandingOrderRepository standingOrders,
    IOrderRepository orders,
    StandingOrderScheduler scheduler,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<WholesaleOptions> wholesale,
    IOptions<OrderingOptions> ordering) :
    IRequestHandler<GetWholesaleCatalogQuery, WholesaleCatalogDto>,
    IRequestHandler<GetStandingOrdersQuery, PagedResult<StandingOrderSummaryDto>>,
    IRequestHandler<GetStandingOrderQuery, StandingOrderDetailsDto?>,
    IRequestHandler<GetMyStandingOrdersQuery, IReadOnlyList<StandingOrderSummaryDto>>,
    IRequestHandler<GetMyStandingOrderQuery, StandingOrderDetailsDto?>,
    IRequestHandler<GetStandingOrderStatementQuery, StatementDto?>,
    IRequestHandler<GetProductionPlanQuery, ProductionPlanDto>
{
    private const int UpcomingDays = 28;

    public async Task<WholesaleCatalogDto> Handle(GetWholesaleCatalogQuery request, CancellationToken cancellationToken)
    {
        var w = wholesale.Value;
        var items = await menu.GetAllAsync(includeUnavailable: false, cancellationToken);
        return new WholesaleCatalogDto
        {
            AcceptingRequests = w.AcceptingRequests,
            Items = items
                .OrderBy(i => i.Category).ThenBy(i => i.SortOrder).ThenBy(i => i.Name)
                .Select(i => new WholesaleItemDto(
                    i.Id, i.Name, i.BengaliName, i.Slug, i.ImageUrl, i.ImageAlt, i.Category, MenuCategoryNames.English(i.Category),
                    i.ShortDescription, i.Unit, i.PricePerUnit, i.EffectiveWholesalePrice(w.DiscountPercent), i.SpiceLevel))
                .ToList(),
            MinimumQuantityPerItem = w.MinimumQuantityPerItem,
            QuantityStep = w.QuantityStep,
            MinimumSubtotalPerDelivery = w.MinimumSubtotalPerDelivery,
            DeliveryFee = w.DeliveryFee,
            DiscountPercent = w.DiscountPercent,
            TaxRate = ordering.Value.TaxRate,
            EarliestStartDate = WholesaleRulesFor.EarliestStart(clock, w),
            TimeSlots = WholesaleRulesFor.Slots(w),
            ClosedDays = ordering.Value.ClosedDays,
            LeadTimeDays = w.LeadTimeDays,
            ChangeCutoffHours = w.ChangeCutoffHours,
            PaymentTerms = w.PaymentTerms,
            DeliveryAreaDescription = w.DeliveryAreaDescription,
        };
    }

    public async Task<PagedResult<StandingOrderSummaryDto>> Handle(GetStandingOrdersQuery request, CancellationToken cancellationToken)
    {
        var (page, size) = Paging.Normalize(request.Page, request.PageSize);
        return await standingOrders.SearchAsync(request.Status, string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(), page, size, cancellationToken);
    }

    public async Task<StandingOrderDetailsDto?> Handle(GetStandingOrderQuery request, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByIdAsync(request.Id, cancellationToken);
        return so is null ? null : scheduler.ToDetails(so, await scheduler.GetUpcomingAsync(so, UpcomingDays, cancellationToken));
    }

    public Task<IReadOnlyList<StandingOrderSummaryDto>> Handle(GetMyStandingOrdersQuery request, CancellationToken cancellationToken) =>
        standingOrders.GetForCustomerAsync(currentUser.UserId!, cancellationToken);

    public async Task<StandingOrderDetailsDto?> Handle(GetMyStandingOrderQuery request, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByReferenceAsync(Normalize(request.Reference), cancellationToken);
        if (so is null || so.CustomerId != currentUser.UserId)
        {
            return null;
        }

        var details = scheduler.ToDetails(so, await scheduler.GetUpcomingAsync(so, UpcomingDays, cancellationToken));
        return details with { AdminNotes = null };
    }

    public async Task<StatementDto?> Handle(GetStandingOrderStatementQuery request, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByIdAsync(request.Id, cancellationToken);
        if (so is null)
        {
            return null;
        }

        var from = request.From <= request.To ? request.From : request.To;
        var to = request.From <= request.To ? request.To : request.From;
        var rows = (await orders.GetForStandingOrderAsync(so.Id, from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MaxValue), cancellationToken))
            .Where(o => o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.ScheduledFor)
            .ToList();

        return new StatementDto(
            scheduler.ToDetails(so, []),
            from,
            to,
            rows,
            rows.Sum(r => r.Subtotal),
            rows.Sum(r => r.DeliveryFee),
            rows.Sum(r => r.Tax),
            rows.Sum(r => r.Total));
    }

    public async Task<ProductionPlanDto> Handle(GetProductionPlanQuery request, CancellationToken cancellationToken)
    {
        var from = request.From ?? DateOnly.FromDateTime(clock.BusinessNow);
        var days = Math.Clamp(request.Days, 1, 31);
        var to = from.AddDays(days - 1);

        var lines = await orders.GetProductionLinesAsync(from.ToDateTime(TimeOnly.MinValue), to.ToDateTime(TimeOnly.MaxValue), cancellationToken);

        // Projected quantities from active standing orders for dates that have no order yet.
        var projected = new List<(DateOnly Date, int MenuItemId, string Name, string Unit, decimal Quantity, int StandingOrderId)>();
        foreach (var so in await standingOrders.GetByStatusAsync(StandingOrderStatus.Active, cancellationToken))
        {
            var ledger = (await standingOrders.GetOccurrencesAsync(so.Id, from, to, cancellationToken)).Select(o => o.Date).ToHashSet();
            foreach (var date in so.OccurrencesBetween(from, to).Where(d => !ledger.Contains(d) && !ordering.Value.IsKitchenClosed(d)))
            {
                projected.AddRange(so.Lines.Select(l => (date, l.MenuItemId, l.ItemName, l.Unit, l.Quantity, so.Id)));
            }
        }

        var result = new List<ProductionDayDto>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayLines = lines.Where(l => DateOnly.FromDateTime(l.ScheduledFor) == date).ToList();
            var dayProjected = projected.Where(p => p.Date == date).ToList();

            var items = dayLines
                .Select(l => (l.MenuItemId, l.ItemName, l.Unit, Online: l.StandingOrderId is null ? l.Quantity : 0m, Restaurant: l.StandingOrderId is null ? 0m : l.Quantity, Projected: 0m))
                .Concat(dayProjected.Select(p => (p.MenuItemId, ItemName: p.Name, p.Unit, Online: 0m, Restaurant: 0m, Projected: p.Quantity)))
                .GroupBy(x => (x.MenuItemId, x.Unit))
                .Select(g => new ProductionItemDto(g.Key.MenuItemId, g.First().ItemName, g.Key.Unit, g.Sum(x => x.Online), g.Sum(x => x.Restaurant), g.Sum(x => x.Projected)))
                .OrderByDescending(i => i.TotalQuantity)
                .ToList();

            result.Add(new ProductionDayDto(
                date,
                ordering.Value.IsKitchenClosed(date),
                dayLines.Where(l => l.StandingOrderId is null).Select(l => l.OrderId).Distinct().Count(),
                dayLines.Where(l => l.StandingOrderId is not null).Select(l => l.OrderId).Distinct().Count(),
                dayProjected.Select(p => p.StandingOrderId).Distinct().Count(),
                items));
        }

        return new ProductionPlanDto(from, to, result);
    }

    private static string Normalize(string reference) => (reference ?? string.Empty).Trim().ToUpperInvariant();
}

internal static class WholesaleRulesFor
{
    public static WholesaleRules Rules(WholesaleOptions w) => new(w.MinimumQuantityPerItem, w.QuantityStep, w.MinimumSubtotalPerDelivery);

    public static IReadOnlyList<TimeOnly> Slots(WholesaleOptions w) =>
        OrderSchedule.BuildSlots(w.FirstSlot, w.LastSlot, w.SlotIntervalMinutes, new TimeOnly(9, 0), new TimeOnly(17, 0));

    public static DateOnly EarliestStart(IDateTimeProvider clock, WholesaleOptions w) =>
        DateOnly.FromDateTime(clock.BusinessNow).AddDays(Math.Max(1, w.LeadTimeDays));
}

internal sealed class WholesaleCommandHandlers(
    IMenuItemRepository menu,
    IStandingOrderRepository standingOrders,
    StandingOrderScheduler scheduler,
    IOrderNumberGenerator numbers,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<WholesaleOptions> wholesale,
    IOptions<OrderingOptions> ordering,
    IPublisher publisher) :
    IRequestHandler<SubmitStandingOrderCommand, string>,
    IRequestHandler<ChangeStandingOrderStatusCommand, string>,
    IRequestHandler<ChangeMyStandingOrderCommand, string>,
    IRequestHandler<SkipStandingOrderDateCommand>,
    IRequestHandler<SkipMyStandingOrderDateCommand>,
    IRequestHandler<UpdateStandingOrderTermsCommand>,
    IRequestHandler<GenerateStandingOrderDeliveriesCommand, GenerationReport>
{
    public async Task<string> Handle(SubmitStandingOrderCommand request, CancellationToken cancellationToken)
    {
        var w = wholesale.Value;
        if (!w.AcceptingRequests)
        {
            throw new ValidationException("We are not taking new restaurant orders right now. Please contact us.");
        }

        var earliest = WholesaleRulesFor.EarliestStart(clock, w);
        if (request.StartDate < earliest)
        {
            throw new ValidationException(nameof(SubmitStandingOrderCommand.StartDate),
                $"The first delivery can be on {earliest.ToString("dddd, MMM d", Format.Culture)} at the earliest ({w.LeadTimeDays} days to plan your order).");
        }

        if (request.StartDate > earliest.AddDays(180))
        {
            throw new ValidationException(nameof(SubmitStandingOrderCommand.StartDate), "Please choose a start date within the next six months.");
        }

        if (!WholesaleRulesFor.Slots(w).Contains(request.PreferredTime))
        {
            throw new ValidationException(nameof(SubmitStandingOrderCommand.PreferredTime), "Please choose one of the available delivery times.");
        }

        var closed = request.Days.Where(d => ordering.Value.ClosedDays.Contains(d)).Distinct().ToList();
        if (closed.Count > 0)
        {
            throw new ValidationException(nameof(SubmitStandingOrderCommand.Days),
                $"Our kitchen is closed on {string.Join(" and ", closed.Select(d => d + "s"))}. Please choose other days.");
        }

        DeliveryAddress? address = null;
        if (request.Fulfillment == FulfillmentMethod.Delivery)
        {
            if (!ordering.Value.DeliversTo(request.PostalCode))
            {
                throw new ValidationException(nameof(SubmitStandingOrderCommand.PostalCode),
                    $"Sorry, we don't deliver to {request.PostalCode} yet. Choose pickup or contact us about delivery.");
            }

            address = new DeliveryAddress(request.AddressLine1!, request.AddressLine2, request.City!, request.State!.ToUpperInvariant(), request.PostalCode!);
        }

        var lines = await BuildLinesAsync(request.Items.Select(i => (i.MenuItemId, i.Quantity, (decimal?)null)), requireAvailable: true, cancellationToken);
        var reference = await UniqueReferenceAsync(cancellationToken);

        StandingOrder standingOrder;
        try
        {
            standingOrder = StandingOrder.Submit(
                reference,
                currentUser.UserId,
                request.BusinessName,
                new CustomerContact(request.ContactName, request.Email, request.Phone),
                request.TaxPermitNumber,
                request.Fulfillment,
                address,
                WeekDaysExtensions.Combine(request.Days),
                request.PreferredTime,
                request.StartDate,
                request.EndDate,
                request.Notes,
                lines,
                request.Fulfillment == FulfillmentMethod.Delivery ? w.DeliveryFee : 0m,
                WholesaleRulesFor.Rules(w),
                clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await standingOrders.AddAsync(standingOrder, cancellationToken);
        await publisher.Publish(new StandingOrderSubmittedNotification(standingOrder.Id), cancellationToken);
        return standingOrder.Reference;
    }

    public async Task<string> Handle(ChangeStandingOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Standing order", request.Id);
        var message = await ApplyAsync(so, request.Action, request.Note, currentUser.Email ?? "Admin", byRestaurant: false, cancellationToken);
        await publisher.Publish(new StandingOrderStatusChangedNotification(so.Id, request.Action, request.Note, ByRestaurant: false, request.NotifyRestaurant), cancellationToken);
        return message;
    }

    public async Task<string> Handle(ChangeMyStandingOrderCommand request, CancellationToken cancellationToken)
    {
        if (request.Action is StandingOrderAction.Approve or StandingOrderAction.Decline)
        {
            throw new ForbiddenAccessException();
        }

        var so = await LoadOwnAsync(request.Reference, cancellationToken);
        var message = await ApplyAsync(so, request.Action, request.Reason, so.ContactName, byRestaurant: true, cancellationToken);
        await publisher.Publish(new StandingOrderStatusChangedNotification(so.Id, request.Action, request.Reason, ByRestaurant: true, NotifyRestaurant: true), cancellationToken);
        return message;
    }

    public async Task Handle(SkipStandingOrderDateCommand request, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Standing order", request.Id);
        await SkipAsync(so, request.Date, request.Skip, currentUser.Email ?? "Admin", cancellationToken);
    }

    public async Task Handle(SkipMyStandingOrderDateCommand request, CancellationToken cancellationToken)
    {
        var so = await LoadOwnAsync(request.Reference, cancellationToken);
        await SkipAsync(so, request.Date, request.Skip, so.ContactName, cancellationToken);
        await publisher.Publish(new StandingOrderDateChangedNotification(so.Id, request.Date, request.Skip), cancellationToken);
    }

    public async Task Handle(UpdateStandingOrderTermsCommand request, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByIdAsync(request.Id, cancellationToken) ?? throw new NotFoundException("Standing order", request.Id);
        var lines = await BuildLinesAsync(request.Lines.Select(l => (l.MenuItemId, l.Quantity, (decimal?)l.UnitPrice)), requireAvailable: false, cancellationToken);
        var by = currentUser.Email ?? "Admin";

        try
        {
            so.UpdateTerms(lines, WeekDaysExtensions.Combine(request.Days), request.PreferredTime, request.StartDate, request.EndDate,
                request.DeliveryFee, request.TaxExempt, WholesaleRulesFor.Rules(wholesale.Value), by, clock.UtcNow);
            so.UpdateAdminNotes(request.AdminNotes, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await standingOrders.UpdateAsync(so, cancellationToken);

        // Re-issue upcoming orders that the kitchen has not started so they reflect the new terms.
        if (so.Status == StandingOrderStatus.Active)
        {
            await scheduler.WithdrawUpcomingAsync(so, null, OccurrenceStatus.Cancelled, "Standing order terms changed", by, cancellationToken);
            await scheduler.ReopenWithdrawnDatesAsync(so, cancellationToken);
            await scheduler.GenerateAsync(so, cancellationToken);
        }
    }

    public Task<GenerationReport> Handle(GenerateStandingOrderDeliveriesCommand request, CancellationToken cancellationToken) =>
        scheduler.GenerateAllAsync(cancellationToken);

    private async Task<string> ApplyAsync(StandingOrder so, StandingOrderAction action, string? note, string by, bool byRestaurant, CancellationToken cancellationToken)
    {
        try
        {
            switch (action)
            {
                case StandingOrderAction.Approve:
                    so.Approve(by, note, clock.UtcNow);
                    break;
                case StandingOrderAction.Decline:
                    so.Decline(by, note, clock.UtcNow);
                    break;
                case StandingOrderAction.Pause:
                    so.Pause(by, note, clock.UtcNow);
                    break;
                case StandingOrderAction.Resume:
                    so.Resume(by, clock.UtcNow);
                    break;
                case StandingOrderAction.Cancel:
                    so.Cancel(by, note, clock.UtcNow);
                    break;
                default:
                    throw new ValidationException("Unknown action.");
            }
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }

        await standingOrders.UpdateAsync(so, cancellationToken);

        var locked = 0;
        switch (action)
        {
            case StandingOrderAction.Approve:
            case StandingOrderAction.Resume:
                await scheduler.ReopenWithdrawnDatesAsync(so, cancellationToken);
                await scheduler.GenerateAsync(so, cancellationToken);
                break;
            case StandingOrderAction.Pause:
            case StandingOrderAction.Cancel:
                (_, locked) = await scheduler.WithdrawUpcomingAsync(
                    so, null, OccurrenceStatus.Cancelled, $"Standing order {(action == StandingOrderAction.Pause ? "paused" : "cancelled")}", by, cancellationToken);
                break;
        }

        var verb = action switch
        {
            StandingOrderAction.Approve => "approved — upcoming deliveries have been scheduled",
            StandingOrderAction.Decline => "declined",
            StandingOrderAction.Pause => "paused",
            StandingOrderAction.Resume => "resumed",
            _ => "cancelled",
        };

        var lockedNote = locked > 0
            ? $" {locked} delivery(ies) within the next {wholesale.Value.ChangeCutoffHours} hours were already being prepared and will still go ahead{(byRestaurant ? " — call us if you need to change them" : string.Empty)}."
            : string.Empty;
        return $"Standing order {so.Reference} {verb}.{lockedNote}";
    }

    private async Task SkipAsync(StandingOrder so, DateOnly date, bool skip, string by, CancellationToken cancellationToken)
    {
        try
        {
            if (skip)
            {
                await scheduler.SkipDateAsync(so, date, by, cancellationToken);
            }
            else
            {
                await scheduler.UnskipDateAsync(so, date, by, cancellationToken);
            }
        }
        catch (DomainException ex)
        {
            throw new ValidationException(ex.Message);
        }
    }

    private async Task<StandingOrder> LoadOwnAsync(string reference, CancellationToken cancellationToken)
    {
        var so = await standingOrders.GetByReferenceAsync((reference ?? string.Empty).Trim().ToUpperInvariant(), cancellationToken);
        if (so is null || so.CustomerId != currentUser.UserId)
        {
            throw new NotFoundException("Standing order", reference ?? string.Empty);
        }

        return so;
    }

    private async Task<List<StandingOrderLineRequest>> BuildLinesAsync(
        IEnumerable<(int MenuItemId, decimal Quantity, decimal? UnitPrice)> inputs, bool requireAvailable, CancellationToken cancellationToken)
    {
        var wanted = inputs.Where(i => i.Quantity > 0).ToList();
        var items = (await menu.GetByIdsAsync(wanted.Select(i => i.MenuItemId).Distinct().ToArray(), cancellationToken)).ToDictionary(i => i.Id);
        var lines = new List<StandingOrderLineRequest>();

        foreach (var input in wanted)
        {
            if (!items.TryGetValue(input.MenuItemId, out var item))
            {
                throw new ValidationException("One of the selected vortas is no longer on our menu. Please refresh the page.");
            }

            if (requireAvailable && !item.IsAvailable)
            {
                throw new ValidationException($"{item.Name} is currently unavailable.");
            }

            var price = input.UnitPrice is { } agreed && agreed > 0 ? agreed : item.EffectiveWholesalePrice(wholesale.Value.DiscountPercent);
            lines.Add(new StandingOrderLineRequest(item.Id, item.Name, item.BengaliName, item.Unit, price, input.Quantity));
        }

        return lines;
    }

    private async Task<string> UniqueReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = numbers.NewStandingOrderReference(clock.BusinessNow);
            if (!await standingOrders.ReferenceExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique standing order reference.");
    }
}
