using MediatR;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Inventory;

/// <summary>Purchases matching the filters (newest first) and the spending figures for this and last month.</summary>
public sealed record GetInventoryOverviewQuery : IRequest<InventoryOverviewDto>, IRequireAdmin
{
    public string? Search { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record GetInventoryPurchaseQuery(int Id) : IRequest<InventoryPurchaseDto?>, IRequireAdmin;

/// <summary>Saved item names (suggested while typing) and units for the add/edit form.</summary>
public sealed record GetInventoryFormOptionsQuery : IRequest<InventoryFormOptionsDto>, IRequireAdmin;

public sealed record GetInventoryItemsQuery : IRequest<IReadOnlyList<InventoryItemSummaryDto>>, IRequireAdmin;

/// <summary>A saved item with its totals and its most recent purchases (price history).</summary>
public sealed record GetInventoryItemQuery(int Id) : IRequest<InventoryItemDetailsDto?>, IRequireAdmin;

/// <summary>Opens a receipt photo; null when the receipt or its file no longer exists.</summary>
public sealed record GetInventoryReceiptFileQuery(int Id) : IRequest<InventoryReceiptFile?>, IRequireAdmin;

internal sealed class InventoryQueryHandlers(IInventoryRepository repository, IReceiptStorage receipts, IDateTimeProvider clock) :
    IRequestHandler<GetInventoryOverviewQuery, InventoryOverviewDto>,
    IRequestHandler<GetInventoryPurchaseQuery, InventoryPurchaseDto?>,
    IRequestHandler<GetInventoryFormOptionsQuery, InventoryFormOptionsDto>,
    IRequestHandler<GetInventoryItemsQuery, IReadOnlyList<InventoryItemSummaryDto>>,
    IRequestHandler<GetInventoryItemQuery, InventoryItemDetailsDto?>,
    IRequestHandler<GetInventoryReceiptFileQuery, InventoryReceiptFile?>
{
    private const int HistorySize = 100;

    public async Task<InventoryOverviewDto> Handle(GetInventoryOverviewQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var purchases = await repository.SearchPurchasesAsync(new InventoryPurchaseSearch
        {
            Search = request.Search,
            From = request.From,
            To = request.To,
            Page = page,
            PageSize = pageSize,
        }, cancellationToken);

        var today = DateOnly.FromDateTime(clock.BusinessNow);
        var thisMonth = new DateTime(today.Year, today.Month, 1);
        var spentThisMonth = await repository.GetSpentAsync(thisMonth, thisMonth.AddMonths(1), cancellationToken);
        var spentLastMonth = await repository.GetSpentAsync(thisMonth.AddMonths(-1), thisMonth, cancellationToken);
        var itemCount = await repository.CountItemsAsync(cancellationToken);

        return new InventoryOverviewDto(purchases, spentThisMonth, spentLastMonth, itemCount);
    }

    public async Task<InventoryPurchaseDto?> Handle(GetInventoryPurchaseQuery request, CancellationToken cancellationToken) =>
        (await repository.GetPurchaseAsync(request.Id, cancellationToken))?.ToDto();

    public async Task<InventoryFormOptionsDto> Handle(GetInventoryFormOptionsQuery request, CancellationToken cancellationToken)
    {
        var items = await repository.GetItemsAsync(cancellationToken);
        var suggestions = items
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(i => new InventoryItemSuggestionDto(i.Name, i.Unit))
            .ToList();

        var units = InventoryUnits.Common
            .Concat(items.Select(i => i.Unit).Where(u => !InventoryUnits.Common.Contains(u)).Distinct().Order(StringComparer.Ordinal))
            .ToList();

        return new InventoryFormOptionsDto(suggestions, units);
    }

    public Task<IReadOnlyList<InventoryItemSummaryDto>> Handle(GetInventoryItemsQuery request, CancellationToken cancellationToken) =>
        repository.GetItemSummariesAsync(cancellationToken: cancellationToken);

    public async Task<InventoryItemDetailsDto?> Handle(GetInventoryItemQuery request, CancellationToken cancellationToken)
    {
        var summary = (await repository.GetItemSummariesAsync(request.Id, cancellationToken)).SingleOrDefault();
        if (summary is null)
        {
            return null;
        }

        var history = await repository.GetItemHistoryAsync(request.Id, HistorySize, cancellationToken);
        return new InventoryItemDetailsDto(summary, history);
    }

    public async Task<InventoryReceiptFile?> Handle(GetInventoryReceiptFileQuery request, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetReceiptAsync(request.Id, cancellationToken);
        if (receipt is null)
        {
            return null;
        }

        var content = await receipts.OpenReadAsync(receipt.FileName, cancellationToken);
        return content is null ? null : new InventoryReceiptFile(content, receipt.ContentType, receipt.OriginalFileName);
    }
}
