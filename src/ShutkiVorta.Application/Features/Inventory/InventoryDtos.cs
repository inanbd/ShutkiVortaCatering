using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.Inventory;

namespace ShutkiVorta.Application.Features.Inventory;

public sealed record InventoryPurchaseSearch
{
    /// <summary>Matches an item name, the store or the notes.</summary>
    public string? Search { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = Paging.DefaultPageSize;
}

/// <summary>A page of purchases and the total of every purchase that matches the filters.</summary>
public sealed record InventoryPurchaseList(PagedResult<InventoryPurchaseSummaryDto> Purchases, decimal Total);

public sealed class InventoryPurchaseSummaryDto
{
    public int Id { get; init; }
    public DateTime PurchasedOn { get; init; }
    public string? Store { get; init; }
    public string? Notes { get; init; }
    public decimal Total { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public int ReceiptCount { get; init; }
    public IReadOnlyList<InventoryLineDto> Lines { get; set; } = [];
}

public sealed class InventoryLineDto
{
    public int PurchaseId { get; init; }
    public int InventoryItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;

    /// <summary>Total paid for the quantity.</summary>
    public decimal Price { get; init; }

    public decimal PricePerUnit => Quantity > 0 ? Money.Round(Price / Quantity) : 0m;
    public string QuantityText => Format.Quantity(Quantity, Unit);
}

public sealed record InventoryReceiptDto(int Id, string? OriginalFileName, string ContentType, long SizeBytes, DateTime UploadedAtUtc)
{
    public string DisplayName => string.IsNullOrWhiteSpace(OriginalFileName) ? $"Receipt {Id}" : OriginalFileName;
}

public sealed record InventoryPurchaseDto
{
    public int Id { get; init; }
    public DateOnly PurchasedOn { get; init; }
    public string? Store { get; init; }
    public string? Notes { get; init; }
    public decimal Total { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public IReadOnlyList<InventoryLineDto> Lines { get; init; } = [];
    public IReadOnlyList<InventoryReceiptDto> Receipts { get; init; } = [];
}

/// <summary>The figures at the top of the inventory page.</summary>
public sealed record InventoryOverviewDto(InventoryPurchaseList Purchases, decimal SpentThisMonth, decimal SpentLastMonth, int SavedItemCount);

/// <summary>A saved item name offered while typing, with the unit to fill in when it is picked.</summary>
public sealed record InventoryItemSuggestionDto(string Name, string Unit);

/// <summary>What the add form needs: saved item names and the units in use.</summary>
public sealed record InventoryFormOptionsDto(IReadOnlyList<InventoryItemSuggestionDto> Items, IReadOnlyList<string> Units);

public sealed class InventoryItemSummaryDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public int TimesBought { get; init; }
    public decimal TotalSpent { get; init; }

    /// <summary>Total quantity bought per unit, e.g. "12 lb" (an item bought in several units lists each).</summary>
    public IReadOnlyList<string> TotalQuantities { get; set; } = [];
    public InventoryItemPurchaseDto? LastPurchase { get; set; }
}

public sealed class InventoryItemPurchaseDto
{
    public int PurchaseId { get; init; }
    public int InventoryItemId { get; init; }
    public DateTime PurchasedOn { get; init; }
    public string? Store { get; init; }
    public decimal Quantity { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal Price { get; init; }

    public decimal PricePerUnit => Quantity > 0 ? Money.Round(Price / Quantity) : 0m;
    public string QuantityText => Format.Quantity(Quantity, Unit);
}

public sealed record InventoryItemDetailsDto(InventoryItemSummaryDto Summary, IReadOnlyList<InventoryItemPurchaseDto> History);

/// <summary>An open receipt photo, streamed to the admin's browser.</summary>
public sealed record InventoryReceiptFile(Stream Content, string ContentType, string? FileName);

/// <summary>Units offered in the unit box, besides those already in use.</summary>
public static class InventoryUnits
{
    public static readonly IReadOnlyList<string> Common =
    [
        "lb", "oz", "kg", "g", "bag", "bottle", "box", "bunch", "can", "case", "dozen", "gallon", "jar", "liter", "pack", "piece", "roll",
    ];
}

public static class InventoryMapping
{
    public static InventoryPurchaseDto ToDto(this InventoryPurchase purchase) => new()
    {
        Id = purchase.Id,
        PurchasedOn = DateOnly.FromDateTime(purchase.PurchasedOn),
        Store = purchase.Store,
        Notes = purchase.Notes,
        Total = purchase.Total,
        CreatedBy = purchase.CreatedBy,
        CreatedAtUtc = purchase.CreatedAtUtc,
        UpdatedAtUtc = purchase.UpdatedAtUtc,
        Lines = purchase.Lines.Select(l => new InventoryLineDto
        {
            PurchaseId = purchase.Id,
            InventoryItemId = l.InventoryItemId,
            ItemName = l.ItemName,
            Quantity = l.Quantity,
            Unit = l.Unit,
            Price = l.Price,
        }).ToList(),
        Receipts = purchase.Receipts
            .Select(r => new InventoryReceiptDto(r.Id, r.OriginalFileName, r.ContentType, r.SizeBytes, r.UploadedAtUtc))
            .ToList(),
    };
}
