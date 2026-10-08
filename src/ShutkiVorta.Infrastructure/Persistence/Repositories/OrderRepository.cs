using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(IDbConnectionFactory connections, ISqlDialect dialect) : IOrderRepository
{
    private const string OrderColumns = """
        Id, OrderNumber, TrackingToken, CustomerId, CustomerName, Email, Phone, Fulfillment, AddressLine1, AddressLine2,
        City, State, PostalCode, ScheduledFor, CustomerNotes, AdminNotes, Status, Subtotal, DeliveryFee, Tax, Total,
        CreatedAtUtc, UpdatedAtUtc, StandingOrderId, CompanyName
        """;

    private const string SummaryColumns = """
        o.Id, o.OrderNumber, o.CustomerId, o.CustomerName, o.Email, o.Phone, o.Fulfillment, o.ScheduledFor, o.Status,
        o.Subtotal, o.DeliveryFee, o.Tax, o.Total, o.CreatedAtUtc, o.StandingOrderId, o.CompanyName
        """;

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        const string insertOrder = """
            INSERT INTO Orders (OrderNumber, TrackingToken, CustomerId, CustomerName, Email, Phone, Fulfillment, AddressLine1,
                AddressLine2, City, State, PostalCode, ScheduledFor, CustomerNotes, AdminNotes, Status, Subtotal, DeliveryFee,
                Tax, Total, CreatedAtUtc, UpdatedAtUtc, StandingOrderId, CompanyName)
            VALUES (@OrderNumber, @TrackingToken, @CustomerId, @CustomerName, @Email, @Phone, @Fulfillment, @AddressLine1,
                @AddressLine2, @City, @State, @PostalCode, @ScheduledFor, @CustomerNotes, @AdminNotes, @Status, @Subtotal, @DeliveryFee,
                @Tax, @Total, @CreatedAtUtc, @UpdatedAtUtc, @StandingOrderId, @CompanyName)
            """;

        const string insertLine = """
            INSERT INTO OrderLines (OrderId, MenuItemId, ItemName, ItemBengaliName, Unit, UnitPrice, Quantity, LineTotal)
            VALUES (@OrderId, @MenuItemId, @ItemName, @ItemBengaliName, @Unit, @UnitPrice, @Quantity, @LineTotal)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var orderId = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(dialect.InsertReturningId(insertOrder), order, transaction, cancellationToken: cancellationToken));
        order.AssignId(orderId);

        foreach (var line in order.Lines)
        {
            line.AttachTo(orderId);
            var lineId = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(dialect.InsertReturningId(insertLine), line, transaction, cancellationToken: cancellationToken));
            line.AssignId(lineId);
        }

        await InsertNewHistoryAsync(connection, transaction, order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Order?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var order = await connection.QuerySingleOrDefaultAsync<Order>(new CommandDefinition(
            $"SELECT {OrderColumns} FROM Orders WHERE OrderNumber = @OrderNumber",
            new { OrderNumber = orderNumber },
            cancellationToken: cancellationToken));

        if (order is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<OrderLine>(new CommandDefinition(
            "SELECT Id, OrderId, MenuItemId, ItemName, ItemBengaliName, Unit, UnitPrice, Quantity, LineTotal FROM OrderLines WHERE OrderId = @Id ORDER BY Id",
            new { order.Id },
            cancellationToken: cancellationToken));

        var history = await connection.QueryAsync<OrderStatusChange>(new CommandDefinition(
            "SELECT Id, OrderId, Status, Note, ChangedBy, ChangedAtUtc FROM OrderStatusChanges WHERE OrderId = @Id ORDER BY ChangedAtUtc, Id",
            new { order.Id },
            cancellationToken: cancellationToken));

        order.Hydrate(lines, history);
        return order;
    }

    public async Task<bool> OrderNumberExistsAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM Orders WHERE OrderNumber = @OrderNumber", new { OrderNumber = orderNumber }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Orders SET Status = @Status, AdminNotes = @AdminNotes, UpdatedAtUtc = @UpdatedAtUtc WHERE Id = @Id",
            new { order.Id, order.Status, order.AdminNotes, order.UpdatedAtUtc },
            transaction,
            cancellationToken: cancellationToken));

        await InsertNewHistoryAsync(connection, transaction, order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PagedResult<OrderSummaryDto>> SearchAsync(OrderSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Replace("%", string.Empty).Replace("_", string.Empty).Replace("[", string.Empty).Trim();
            conditions.Add("(o.OrderNumber LIKE @Pattern OR o.CustomerName LIKE @Pattern OR o.CompanyName LIKE @Pattern OR o.Email LIKE @Pattern OR o.Phone LIKE @Pattern)");
            parameters.Add("Pattern", $"%{term}%");
        }

        if (criteria.Status is { } status)
        {
            conditions.Add("o.Status = @Status");
            parameters.Add("Status", (int)status);
        }

        if (criteria.Fulfillment is { } fulfillment)
        {
            conditions.Add("o.Fulfillment = @Fulfillment");
            parameters.Add("Fulfillment", (int)fulfillment);
        }

        if (criteria.Source is { } source)
        {
            conditions.Add(source == OrderSource.Restaurant ? "o.StandingOrderId IS NOT NULL" : "o.StandingOrderId IS NULL");
        }

        if (criteria.ScheduledFrom is { } from)
        {
            conditions.Add("o.ScheduledFor >= @From");
            parameters.Add("From", from);
        }

        if (criteria.ScheduledTo is { } to)
        {
            conditions.Add("o.ScheduledFor <= @To");
            parameters.Add("To", to);
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Skip", (criteria.Page - 1) * criteria.PageSize);
        parameters.Add("Take", criteria.PageSize);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM Orders o {where}", parameters, cancellationToken: cancellationToken));

        if (total == 0)
        {
            return PagedResult<OrderSummaryDto>.Empty(criteria.Page, criteria.PageSize);
        }

        var sql = dialect.Page($"SELECT {SummaryColumns} FROM Orders o {where} ORDER BY o.CreatedAtUtc DESC, o.Id DESC");
        var items = (await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();
        await AttachPreviewsAsync(connection, items, cancellationToken);

        return new PagedResult<OrderSummaryDto>(items, total, criteria.Page, criteria.PageSize);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetForCustomerAsync(string customerId, string? verifiedEmail, CancellationToken cancellationToken = default)
    {
        var sql = verifiedEmail is null
            ? $"SELECT {SummaryColumns} FROM Orders o WHERE o.CustomerId = @CustomerId ORDER BY o.CreatedAtUtc DESC"
            : $"SELECT {SummaryColumns} FROM Orders o WHERE o.CustomerId = @CustomerId OR (o.CustomerId IS NULL AND o.Email = @Email) ORDER BY o.CreatedAtUtc DESC";

        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = (await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(
            sql, new { CustomerId = customerId, Email = verifiedEmail?.ToLowerInvariant() }, cancellationToken: cancellationToken))).AsList();
        await AttachPreviewsAsync(connection, items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetCreatedSinceAsync(DateTime fromUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return (await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM Orders o WHERE o.CreatedAtUtc >= @FromUtc ORDER BY o.CreatedAtUtc DESC",
            new { FromUtc = fromUtc },
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetScheduledBetweenAsync(DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = (await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM Orders o WHERE o.ScheduledFor >= @From AND o.ScheduledFor <= @To ORDER BY o.ScheduledFor",
            new { From = fromLocal, To = toLocal },
            cancellationToken: cancellationToken))).AsList();
        await AttachPreviewsAsync(connection, items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyDictionary<OrderStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<StatusCount>(new CommandDefinition(
            "SELECT Status, COUNT(*) AS Total FROM Orders GROUP BY Status", cancellationToken: cancellationToken));
        return rows.ToDictionary(r => (OrderStatus)r.Status, r => r.Total);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountByCustomerAsync(IReadOnlyCollection<string> customerIds, CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<string, int>();
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CustomerCount>(new CommandDefinition(
            "SELECT CustomerId, COUNT(*) AS Total FROM Orders WHERE CustomerId IN @Ids GROUP BY CustomerId",
            new { Ids = customerIds.ToArray() },
            cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.CustomerId, r => r.Total);
    }

    public async Task<IReadOnlyList<PopularItemDto>> GetPopularItemsSinceAsync(DateTime fromUtc, int take, CancellationToken cancellationToken = default)
    {
        var sql = dialect.Page("""
            SELECT l.ItemName AS ItemName, l.Unit AS Unit, SUM(l.Quantity) AS TotalQuantity, SUM(l.LineTotal) AS Revenue
            FROM OrderLines l
            INNER JOIN Orders o ON o.Id = l.OrderId
            WHERE o.CreatedAtUtc >= @FromUtc AND o.Status <> @Cancelled
            GROUP BY l.ItemName, l.Unit
            ORDER BY SUM(l.Quantity) DESC, l.ItemName
            """);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PopularItemDto>(new CommandDefinition(
            sql,
            new { FromUtc = fromUtc, Cancelled = (int)OrderStatus.Cancelled, Skip = 0, Take = take },
            cancellationToken: cancellationToken));
        return rows.Select(r => r with { Revenue = Math.Round(r.Revenue, 2), TotalQuantity = Math.Round(r.TotalQuantity, 2) }).ToList();
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetForStandingOrderAsync(int standingOrderId, DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = (await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM Orders o WHERE o.StandingOrderId = @StandingOrderId AND o.ScheduledFor >= @From AND o.ScheduledFor <= @To ORDER BY o.ScheduledFor",
            new { StandingOrderId = standingOrderId, From = fromLocal, To = toLocal },
            cancellationToken: cancellationToken))).AsList();
        await AttachPreviewsAsync(connection, items, cancellationToken);
        return items;
    }

    public async Task<IReadOnlyList<ProductionLine>> GetProductionLinesAsync(DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProductionLine>(new CommandDefinition(
            """
            SELECT o.Id AS OrderId, o.StandingOrderId, o.ScheduledFor, l.MenuItemId, l.ItemName, l.Unit, l.Quantity
            FROM OrderLines l
            INNER JOIN Orders o ON o.Id = l.OrderId
            WHERE o.ScheduledFor >= @From AND o.ScheduledFor <= @To AND o.Status <> @Cancelled
            ORDER BY o.ScheduledFor, l.Id
            """,
            new { From = fromLocal, To = toLocal, Cancelled = (int)OrderStatus.Cancelled },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private async Task InsertNewHistoryAsync(DbConnection connection, DbTransaction transaction, Order order, CancellationToken cancellationToken)
    {
        const string insertHistory = """
            INSERT INTO OrderStatusChanges (OrderId, Status, Note, ChangedBy, ChangedAtUtc)
            VALUES (@OrderId, @Status, @Note, @ChangedBy, @ChangedAtUtc)
            """;

        foreach (var change in order.History.Where(h => h.IsTransient))
        {
            change.AttachTo(order.Id);
            var id = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(dialect.InsertReturningId(insertHistory), change, transaction, cancellationToken: cancellationToken));
            change.AssignId(id);
        }
    }

    /// <summary>Adds a short "2 lb Loitta Shutki Vorta, 1 lb Aloo Vorta" description to each summary row.</summary>
    private static async Task AttachPreviewsAsync(DbConnection connection, List<OrderSummaryDto> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0)
        {
            return;
        }

        var lines = await connection.QueryAsync<PreviewLine>(new CommandDefinition(
            "SELECT OrderId, ItemName, Quantity, Unit FROM OrderLines WHERE OrderId IN @Ids ORDER BY Id",
            new { Ids = orders.Select(o => o.Id).ToArray() },
            cancellationToken: cancellationToken));

        var byOrder = lines.GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var order in orders)
        {
            if (byOrder.TryGetValue(order.Id, out var orderLines))
            {
                order.ItemsPreview = string.Join(", ", orderLines.Select(l => $"{Format.Quantity(l.Quantity, l.Unit)} {l.ItemName}"));
            }
        }
    }

    private sealed class StatusCount
    {
        public int Status { get; init; }
        public int Total { get; init; }
    }

    private sealed class CustomerCount
    {
        public string CustomerId { get; init; } = string.Empty;
        public int Total { get; init; }
    }

    private sealed class PreviewLine
    {
        public int OrderId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public string Unit { get; init; } = string.Empty;
    }
}
