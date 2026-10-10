using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Inventory;
using ShutkiVorta.Domain.Inventory;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

internal sealed class InventoryRepository(IDbConnectionFactory connections, ISqlDialect dialect) : IInventoryRepository
{
    private const string PurchaseColumns = "Id, PurchasedOn, Store, Notes, Total, CreatedBy, CreatedAtUtc, UpdatedAtUtc";
    private const string ItemColumns = "Id, Name, NormalizedName, Unit, CreatedAtUtc, UpdatedAtUtc";
    private const string ReceiptColumns = "Id, PurchaseId, FileName, OriginalFileName, ContentType, SizeBytes, UploadedAtUtc";

    // Lines read the item's current name, so renaming a saved item corrects every purchase.
    private const string LineSelect = """
        SELECT l.Id, l.PurchaseId, l.InventoryItemId, i.Name AS ItemName, l.Quantity, l.Unit, l.Price
        FROM InventoryPurchaseLines l JOIN InventoryItems i ON i.Id = l.InventoryItemId
        """;

    private const string ItemPurchaseSelect = """
        SELECT l.PurchaseId, l.InventoryItemId, p.PurchasedOn, p.Store, l.Quantity, l.Unit, l.Price
        FROM InventoryPurchaseLines l JOIN InventoryPurchases p ON p.Id = l.PurchaseId
        """;

    // ---- Purchases ----

    public async Task<InventoryPurchase?> GetPurchaseAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var purchase = await connection.QuerySingleOrDefaultAsync<InventoryPurchase>(new CommandDefinition(
            $"SELECT {PurchaseColumns} FROM InventoryPurchases WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
        if (purchase is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<InventoryPurchaseLine>(new CommandDefinition(
            $"{LineSelect} WHERE l.PurchaseId = @Id ORDER BY l.Id", new { Id = id }, cancellationToken: cancellationToken));
        var receipts = await connection.QueryAsync<InventoryReceipt>(new CommandDefinition(
            $"SELECT {ReceiptColumns} FROM InventoryReceipts WHERE PurchaseId = @Id ORDER BY Id", new { Id = id }, cancellationToken: cancellationToken));

        purchase.Hydrate(lines, receipts);
        return purchase;
    }

    public async Task AddPurchaseAsync(InventoryPurchase purchase, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO InventoryPurchases (PurchasedOn, Store, Notes, Total, CreatedBy, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@PurchasedOn, @Store, @Notes, @Total, @CreatedBy, @CreatedAtUtc, @UpdatedAtUtc)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(sql), purchase, transaction, cancellationToken: cancellationToken));
        purchase.AssignId(id);

        await InsertLinesAsync(connection, transaction, purchase, rememberUnits: true, cancellationToken);
        await InsertNewReceiptsAsync(connection, transaction, purchase, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdatePurchaseAsync(InventoryPurchase purchase, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE InventoryPurchases SET PurchasedOn = @PurchasedOn, Store = @Store, Notes = @Notes, Total = @Total, UpdatedAtUtc = @UpdatedAtUtc
            WHERE Id = @Id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(sql, purchase, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM InventoryPurchaseLines WHERE PurchaseId = @Id", new { purchase.Id }, transaction, cancellationToken: cancellationToken));

        // Editing an old purchase should not change the unit suggested for new ones.
        await InsertLinesAsync(connection, transaction, purchase, rememberUnits: false, cancellationToken);

        var kept = purchase.Receipts.Where(r => !r.IsTransient).Select(r => r.Id).ToArray();
        await connection.ExecuteAsync(new CommandDefinition(
            kept.Length == 0
                ? "DELETE FROM InventoryReceipts WHERE PurchaseId = @Id"
                : "DELETE FROM InventoryReceipts WHERE PurchaseId = @Id AND Id NOT IN @Kept",
            new { purchase.Id, Kept = kept },
            transaction,
            cancellationToken: cancellationToken));
        await InsertNewReceiptsAsync(connection, transaction, purchase, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeletePurchaseAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var sql in new[]
                 {
                     "DELETE FROM InventoryPurchaseLines WHERE PurchaseId = @Id",
                     "DELETE FROM InventoryReceipts WHERE PurchaseId = @Id",
                     "DELETE FROM InventoryPurchases WHERE Id = @Id",
                 })
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<InventoryPurchaseList> SearchPurchasesAsync(InventoryPurchaseSearch search, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            var term = search.Search.Replace("%", string.Empty).Replace("_", string.Empty).Replace("[", string.Empty).Trim();
            conditions.Add("""
                (p.Store LIKE @Pattern OR p.Notes LIKE @Pattern OR EXISTS (
                    SELECT 1 FROM InventoryPurchaseLines sl JOIN InventoryItems si ON si.Id = sl.InventoryItemId
                    WHERE sl.PurchaseId = p.Id AND si.NormalizedName LIKE @NamePattern))
                """);
            parameters.Add("Pattern", $"%{term}%");
            parameters.Add("NamePattern", $"%{term.ToUpperInvariant()}%");
        }

        if (search.From is { } from)
        {
            conditions.Add("p.PurchasedOn >= @From");
            parameters.Add("From", from.ToDateTime(TimeOnly.MinValue));
        }

        if (search.To is { } to)
        {
            conditions.Add("p.PurchasedOn < @ToExclusive");
            parameters.Add("ToExclusive", to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Skip", (search.Page - 1) * search.PageSize);
        parameters.Add("Take", search.PageSize);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var totals = await connection.QuerySingleAsync<Totals>(new CommandDefinition(
            $"SELECT COUNT(*) AS PurchaseCount, COALESCE(SUM(p.Total), 0) AS Amount FROM InventoryPurchases p {where}", parameters, cancellationToken: cancellationToken));

        if (totals.PurchaseCount == 0)
        {
            return new InventoryPurchaseList(PagedResult<InventoryPurchaseSummaryDto>.Empty(search.Page, search.PageSize), 0m);
        }

        var sql = dialect.Page($"""
            SELECT p.Id, p.PurchasedOn, p.Store, p.Notes, p.Total, p.CreatedBy,
                   (SELECT COUNT(*) FROM InventoryReceipts r WHERE r.PurchaseId = p.Id) AS ReceiptCount
            FROM InventoryPurchases p {where}
            ORDER BY p.PurchasedOn DESC, p.Id DESC
            """);
        var purchases = (await connection.QueryAsync<InventoryPurchaseSummaryDto>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();

        if (purchases.Count > 0)
        {
            var lines = await connection.QueryAsync<InventoryLineDto>(new CommandDefinition(
                $"{LineSelect} WHERE l.PurchaseId IN @Ids ORDER BY l.Id",
                new { Ids = purchases.Select(p => p.Id).ToArray() },
                cancellationToken: cancellationToken));
            var byPurchase = lines.GroupBy(l => l.PurchaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<InventoryLineDto>)g.ToList());
            foreach (var purchase in purchases)
            {
                purchase.Lines = byPurchase.GetValueOrDefault(purchase.Id, []);
            }
        }

        return new InventoryPurchaseList(
            new PagedResult<InventoryPurchaseSummaryDto>(purchases, totals.PurchaseCount, search.Page, search.PageSize), totals.Amount);
    }

    public async Task<decimal> GetSpentAsync(DateTime from, DateTime toExclusive, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(Total), 0) FROM InventoryPurchases WHERE PurchasedOn >= @From AND PurchasedOn < @To",
            new { From = from, To = toExclusive },
            cancellationToken: cancellationToken));
    }

    public async Task<InventoryReceipt?> GetReceiptAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InventoryReceipt>(new CommandDefinition(
            $"SELECT {ReceiptColumns} FROM InventoryReceipts WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    // ---- Saved items ----

    public async Task<IReadOnlyList<InventoryItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = await connection.QueryAsync<InventoryItem>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM InventoryItems ORDER BY Name", cancellationToken: cancellationToken));
        return items.AsList();
    }

    public async Task<IReadOnlyList<InventoryItemSummaryDto>> GetItemSummariesAsync(int? itemId = null, CancellationToken cancellationToken = default)
    {
        var itemFilter = itemId is null ? string.Empty : "WHERE i.Id = @ItemId";
        var lineFilter = itemId is null ? string.Empty : "WHERE l.InventoryItemId = @ItemId";
        var parameters = new { ItemId = itemId };

        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = (await connection.QueryAsync<InventoryItemSummaryDto>(new CommandDefinition(
            $"""
            SELECT i.Id, i.Name, i.Unit, COUNT(DISTINCT l.PurchaseId) AS TimesBought, COALESCE(SUM(l.Price), 0) AS TotalSpent
            FROM InventoryItems i LEFT JOIN InventoryPurchaseLines l ON l.InventoryItemId = i.Id
            {itemFilter}
            GROUP BY i.Id, i.Name, i.Unit
            """,
            parameters,
            cancellationToken: cancellationToken))).AsList();

        var quantities = (await connection.QueryAsync<UnitTotal>(new CommandDefinition(
            $"SELECT l.InventoryItemId, l.Unit, SUM(l.Quantity) AS Quantity FROM InventoryPurchaseLines l {lineFilter} GROUP BY l.InventoryItemId, l.Unit",
            parameters,
            cancellationToken: cancellationToken))).ToLookup(q => q.InventoryItemId);

        var latest = (await connection.QueryAsync<InventoryItemPurchaseDto>(new CommandDefinition(
            $"""
            SELECT PurchaseId, InventoryItemId, PurchasedOn, Store, Quantity, Unit, Price FROM (
                SELECT l.PurchaseId, l.InventoryItemId, p.PurchasedOn, p.Store, l.Quantity, l.Unit, l.Price,
                       ROW_NUMBER() OVER (PARTITION BY l.InventoryItemId ORDER BY p.PurchasedOn DESC, l.Id DESC) AS RowNumber
                FROM InventoryPurchaseLines l JOIN InventoryPurchases p ON p.Id = l.PurchaseId
                {lineFilter}
            ) latest
            WHERE RowNumber = 1
            """,
            parameters,
            cancellationToken: cancellationToken))).ToDictionary(p => p.InventoryItemId);

        foreach (var item in items)
        {
            item.TotalQuantities = quantities[item.Id]
                .OrderByDescending(q => q.Quantity)
                .Select(q => Format.Quantity(q.Quantity, q.Unit))
                .ToList();
            item.LastPurchase = latest.GetValueOrDefault(item.Id);
        }

        return items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<InventoryItem?> GetItemAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InventoryItem>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM InventoryItems WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<InventoryItem?> GetItemByNormalizedNameAsync(string normalizedName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InventoryItem>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM InventoryItems WHERE NormalizedName = @NormalizedName",
            new { NormalizedName = normalizedName },
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM InventoryItems", cancellationToken: cancellationToken));
    }

    public async Task AddItemAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await InsertItemAsync(connection, null, item, cancellationToken);
    }

    public async Task UpdateItemAsync(InventoryItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE InventoryItems SET Name = @Name, NormalizedName = @NormalizedName, Unit = @Unit, UpdatedAtUtc = @UpdatedAtUtc WHERE Id = @Id",
            item,
            cancellationToken: cancellationToken));
    }

    public async Task<int> CountPurchasesWithItemAsync(int itemId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(DISTINCT PurchaseId) FROM InventoryPurchaseLines WHERE InventoryItemId = @Id", new { Id = itemId }, cancellationToken: cancellationToken));
    }

    public async Task DeleteItemAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM InventoryItems WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<InventoryItemPurchaseDto>> GetItemHistoryAsync(int itemId, int take, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var history = await connection.QueryAsync<InventoryItemPurchaseDto>(new CommandDefinition(
            dialect.Page($"{ItemPurchaseSelect} WHERE l.InventoryItemId = @ItemId ORDER BY p.PurchasedOn DESC, l.Id DESC"),
            new { ItemId = itemId, Skip = 0, Take = take },
            cancellationToken: cancellationToken));
        return history.AsList();
    }

    // ---- Helpers ----

    /// <summary>
    /// Inserts the purchase lines, linking each to its saved item by name and saving names used for the first time.
    /// With <paramref name="rememberUnits"/>, an existing item also remembers the unit used, to suggest it next time.
    /// </summary>
    private async Task InsertLinesAsync(DbConnection connection, DbTransaction transaction, InventoryPurchase purchase, bool rememberUnits, CancellationToken cancellationToken)
    {
        const string insertLine = """
            INSERT INTO InventoryPurchaseLines (PurchaseId, InventoryItemId, Quantity, Unit, Price)
            VALUES (@PurchaseId, @InventoryItemId, @Quantity, @Unit, @Price)
            """;

        foreach (var line in purchase.Lines)
        {
            var itemId = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT Id FROM InventoryItems WHERE NormalizedName = @NormalizedName",
                new { NormalizedName = line.ItemName.ToUpperInvariant() },
                transaction,
                cancellationToken: cancellationToken));

            if (itemId is null)
            {
                var item = InventoryItem.Create(line.ItemName, line.Unit, purchase.UpdatedAtUtc);
                await InsertItemAsync(connection, transaction, item, cancellationToken);
                itemId = item.Id;
            }
            else if (rememberUnits)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE InventoryItems SET Unit = @Unit, UpdatedAtUtc = @UpdatedAtUtc WHERE Id = @Id AND Unit <> @Unit",
                    new { Id = itemId, line.Unit, purchase.UpdatedAtUtc },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            line.AttachTo(purchase.Id);
            line.LinkTo(itemId.Value);
            var lineId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                dialect.InsertReturningId(insertLine), line, transaction, cancellationToken: cancellationToken));
            line.AssignId(lineId);
        }
    }

    private async Task InsertNewReceiptsAsync(DbConnection connection, DbTransaction transaction, InventoryPurchase purchase, CancellationToken cancellationToken)
    {
        const string insertReceipt = """
            INSERT INTO InventoryReceipts (PurchaseId, FileName, OriginalFileName, ContentType, SizeBytes, UploadedAtUtc)
            VALUES (@PurchaseId, @FileName, @OriginalFileName, @ContentType, @SizeBytes, @UploadedAtUtc)
            """;

        foreach (var receipt in purchase.Receipts.Where(r => r.IsTransient))
        {
            receipt.AttachTo(purchase.Id);
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                dialect.InsertReturningId(insertReceipt), receipt, transaction, cancellationToken: cancellationToken));
            receipt.AssignId(id);
        }
    }

    private async Task InsertItemAsync(DbConnection connection, DbTransaction? transaction, InventoryItem item, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO InventoryItems (Name, NormalizedName, Unit, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@Name, @NormalizedName, @Unit, @CreatedAtUtc, @UpdatedAtUtc)
            """;

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(sql), item, transaction, cancellationToken: cancellationToken));
        item.AssignId(id);
    }

    private sealed class Totals
    {
        public int PurchaseCount { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class UnitTotal
    {
        public int InventoryItemId { get; init; }
        public string Unit { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
    }
}
