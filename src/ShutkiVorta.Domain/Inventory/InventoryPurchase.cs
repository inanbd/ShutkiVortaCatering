using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.Inventory;

/// <summary>Ingredients and supplies added to the kitchen's inventory on one day (usually one receipt), with receipt photos.</summary>
public sealed class InventoryPurchase : Entity
{
    public const int MaxLines = 50;
    public const int MaxReceipts = 10;

    private readonly List<InventoryPurchaseLine> _lines = [];
    private readonly List<InventoryReceipt> _receipts = [];

    // Required by the data mapper.
    private InventoryPurchase()
    {
    }

    /// <summary>The day the items were bought (business time zone, midnight).</summary>
    public DateTime PurchasedOn { get; private set; }

    public string? Store { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Sum of the line prices.</summary>
    public decimal Total { get; private set; }

    public string CreatedBy { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<InventoryPurchaseLine> Lines => _lines;
    public IReadOnlyList<InventoryReceipt> Receipts => _receipts;

    /// <param name="today">The current date at the business, so a purchase cannot be dated in the future.</param>
    public static InventoryPurchase Record(
        DateOnly purchasedOn, string? store, string? notes, IReadOnlyCollection<InventoryLineRequest> lines,
        string createdBy, DateOnly today, DateTime nowUtc)
    {
        var purchase = new InventoryPurchase
        {
            CreatedBy = Guard.NotEmpty(createdBy, "Created by", 256),
            CreatedAtUtc = nowUtc,
        };
        purchase.Apply(purchasedOn, store, notes, lines, today, nowUtc);
        return purchase;
    }

    /// <summary>Replaces the date, store, notes and every line. Receipts are kept.</summary>
    public void Update(DateOnly purchasedOn, string? store, string? notes, IReadOnlyCollection<InventoryLineRequest> lines, DateOnly today, DateTime nowUtc) =>
        Apply(purchasedOn, store, notes, lines, today, nowUtc);

    public InventoryReceipt AddReceipt(string fileName, string? originalFileName, string contentType, long sizeBytes, DateTime nowUtc)
    {
        if (_receipts.Count >= MaxReceipts)
        {
            throw new DomainException($"A purchase can have at most {MaxReceipts} receipt photos.");
        }

        var receipt = InventoryReceipt.Create(fileName, originalFileName, contentType, sizeBytes, nowUtc);
        _receipts.Add(receipt);
        UpdatedAtUtc = nowUtc;
        return receipt;
    }

    /// <summary>Removes a receipt and returns it so its file can be deleted; null when it does not belong to this purchase.</summary>
    public InventoryReceipt? RemoveReceipt(int receiptId, DateTime nowUtc)
    {
        var receipt = _receipts.Find(r => r.Id == receiptId && !r.IsTransient);
        if (receipt is null)
        {
            return null;
        }

        _receipts.Remove(receipt);
        UpdatedAtUtc = nowUtc;
        return receipt;
    }

    public void Hydrate(IEnumerable<InventoryPurchaseLine> lines, IEnumerable<InventoryReceipt> receipts)
    {
        _lines.Clear();
        _lines.AddRange(lines);
        _receipts.Clear();
        _receipts.AddRange(receipts);
    }

    private void Apply(DateOnly purchasedOn, string? store, string? notes, IReadOnlyCollection<InventoryLineRequest> lines, DateOnly today, DateTime nowUtc)
    {
        if (purchasedOn > today)
        {
            throw new DomainException("The purchase date cannot be in the future.");
        }

        if (purchasedOn.Year < 2000)
        {
            throw new DomainException("Please enter a valid purchase date.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("Add at least one item.");
        }

        if (lines.Count > MaxLines)
        {
            throw new DomainException($"A purchase can have at most {MaxLines} items. Please split it into two.");
        }

        var newLines = lines.Select(InventoryPurchaseLine.Create).ToList();
        PurchasedOn = purchasedOn.ToDateTime(TimeOnly.MinValue);
        Store = Guard.Optional(store, "Store", 120);
        Notes = Guard.Optional(notes, "Notes", 1000);
        _lines.Clear();
        _lines.AddRange(newLines);
        Total = Money.Round(_lines.Sum(l => l.Price));
        UpdatedAtUtc = nowUtc;
    }
}
