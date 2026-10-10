using ShutkiVorta.Domain.Inventory;

namespace ShutkiVorta.Application.Features.Inventory;

public interface IInventoryRepository
{
    // ---- Purchases ----

    /// <summary>A purchase with its lines and receipts.</summary>
    Task<InventoryPurchase?> GetPurchaseAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the purchase, its lines and receipts in one transaction. Each line's item is looked up by name and created
    /// when the name is new, so it is suggested next time; the item remembers the unit used last.
    /// </summary>
    Task AddPurchaseAsync(InventoryPurchase purchase, CancellationToken cancellationToken = default);

    /// <summary>Saves the purchase details, replaces its lines and inserts new or removes deleted receipts, in one transaction.</summary>
    Task UpdatePurchaseAsync(InventoryPurchase purchase, CancellationToken cancellationToken = default);

    /// <summary>Deletes the purchase with its lines and receipt records (not the receipt files).</summary>
    Task DeletePurchaseAsync(int id, CancellationToken cancellationToken = default);

    Task<InventoryPurchaseList> SearchPurchasesAsync(InventoryPurchaseSearch search, CancellationToken cancellationToken = default);

    /// <summary>Total of the purchases made on or after <paramref name="from"/> and before <paramref name="toExclusive"/>.</summary>
    Task<decimal> GetSpentAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken = default);

    Task<InventoryReceipt?> GetReceiptAsync(int id, CancellationToken cancellationToken = default);

    // ---- Saved items ----

    Task<IReadOnlyList<InventoryItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saved items (all, or just <paramref name="itemId"/>) with what has been bought of each: totals per unit, total spent
    /// and the latest purchase. Sorted by name.
    /// </summary>
    Task<IReadOnlyList<InventoryItemSummaryDto>> GetItemSummariesAsync(int? itemId = null, CancellationToken cancellationToken = default);

    Task<InventoryItem?> GetItemAsync(int id, CancellationToken cancellationToken = default);
    Task<InventoryItem?> GetItemByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default);
    Task<int> CountItemsAsync(CancellationToken cancellationToken = default);
    Task AddItemAsync(InventoryItem item, CancellationToken cancellationToken = default);
    Task UpdateItemAsync(InventoryItem item, CancellationToken cancellationToken = default);

    /// <summary>How many purchases include the item (an item in use cannot be deleted).</summary>
    Task<int> CountPurchasesWithItemAsync(int itemId, CancellationToken cancellationToken = default);

    Task DeleteItemAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The most recent purchases of an item, newest first.</summary>
    Task<IReadOnlyList<InventoryItemPurchaseDto>> GetItemHistoryAsync(int itemId, int take, CancellationToken cancellationToken = default);
}
