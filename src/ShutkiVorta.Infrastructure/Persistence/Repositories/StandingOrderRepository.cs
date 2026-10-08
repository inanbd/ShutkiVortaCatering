using System.Data.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Wholesale;
using ShutkiVorta.Domain.Wholesale;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

internal sealed class StandingOrderRepository(IDbConnectionFactory connections, ISqlDialect dialect) : IStandingOrderRepository
{
    private const string Columns = """
        Id, Reference, CustomerId, BusinessName, ContactName, Email, Phone, TaxPermitNumber, Fulfillment, AddressLine1,
        AddressLine2, City, State, PostalCode, DaysOfWeek, PreferredTimeMinutes, StartDate, EndDate, Notes, AdminNotes,
        Status, StatusReason, TaxExempt, DeliveryFee, CreatedAtUtc, UpdatedAtUtc, ApprovedAtUtc
        """;

    private const string SummaryColumns = """
        s.Id, s.Reference, s.CustomerId, s.BusinessName, s.ContactName, s.Email, s.Phone, s.Fulfillment, s.DaysOfWeek,
        s.PreferredTimeMinutes, s.StartDate, s.EndDate, s.Status, s.TaxExempt, s.CreatedAtUtc
        """;

    public async Task AddAsync(StandingOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO StandingOrders (Reference, CustomerId, BusinessName, ContactName, Email, Phone, TaxPermitNumber, Fulfillment,
                AddressLine1, AddressLine2, City, State, PostalCode, DaysOfWeek, PreferredTimeMinutes, StartDate, EndDate, Notes,
                AdminNotes, Status, StatusReason, TaxExempt, DeliveryFee, CreatedAtUtc, UpdatedAtUtc, ApprovedAtUtc)
            VALUES (@Reference, @CustomerId, @BusinessName, @ContactName, @Email, @Phone, @TaxPermitNumber, @Fulfillment,
                @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @DaysOfWeek, @PreferredTimeMinutes, @StartDate, @EndDate, @Notes,
                @AdminNotes, @Status, @StatusReason, @TaxExempt, @DeliveryFee, @CreatedAtUtc, @UpdatedAtUtc, @ApprovedAtUtc)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            dialect.InsertReturningId(sql), Parameters(order), transaction, cancellationToken: cancellationToken));
        order.AssignId(id);

        await InsertLinesAsync(connection, transaction, order, cancellationToken);
        await InsertNewEventsAsync(connection, transaction, order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(StandingOrder order, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE StandingOrders SET
                BusinessName = @BusinessName, ContactName = @ContactName, Email = @Email, Phone = @Phone,
                TaxPermitNumber = @TaxPermitNumber, Fulfillment = @Fulfillment, AddressLine1 = @AddressLine1,
                AddressLine2 = @AddressLine2, City = @City, State = @State, PostalCode = @PostalCode, DaysOfWeek = @DaysOfWeek,
                PreferredTimeMinutes = @PreferredTimeMinutes, StartDate = @StartDate, EndDate = @EndDate, Notes = @Notes,
                AdminNotes = @AdminNotes, Status = @Status, StatusReason = @StatusReason, TaxExempt = @TaxExempt,
                DeliveryFee = @DeliveryFee, UpdatedAtUtc = @UpdatedAtUtc, ApprovedAtUtc = @ApprovedAtUtc
            WHERE Id = @Id
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(sql, Parameters(order), transaction, cancellationToken: cancellationToken));

        // Lines are a value list: replace them whenever the aggregate is saved.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM StandingOrderLines WHERE StandingOrderId = @Id", new { order.Id }, transaction, cancellationToken: cancellationToken));
        await InsertLinesAsync(connection, transaction, order, cancellationToken);
        await InsertNewEventsAsync(connection, transaction, order, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<StandingOrder?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        await LoadAsync("Id = @Value", id, cancellationToken);

    public async Task<StandingOrder?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        await LoadAsync("Reference = @Value", reference, cancellationToken);

    public async Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM StandingOrders WHERE Reference = @Reference", new { Reference = reference }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlyList<StandingOrder>> GetByStatusAsync(StandingOrderStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var orders = (await connection.QueryAsync<StandingOrder>(new CommandDefinition(
            $"SELECT {Columns} FROM StandingOrders WHERE Status = @Status ORDER BY Id",
            new { Status = (int)status },
            cancellationToken: cancellationToken))).AsList();

        if (orders.Count == 0)
        {
            return orders;
        }

        var ids = orders.Select(o => o.Id).ToArray();
        var lines = (await connection.QueryAsync<StandingOrderLine>(new CommandDefinition(
            "SELECT Id, StandingOrderId, MenuItemId, ItemName, ItemBengaliName, Unit, UnitPrice, Quantity FROM StandingOrderLines WHERE StandingOrderId IN @Ids ORDER BY Id",
            new { Ids = ids },
            cancellationToken: cancellationToken))).ToLookup(l => l.StandingOrderId);

        foreach (var order in orders)
        {
            // Events are not needed for scheduling; load the full aggregate by id when they are.
            order.Hydrate(lines[order.Id], []);
        }

        return orders;
    }

    public async Task<PagedResult<StandingOrderSummaryDto>> SearchAsync(StandingOrderStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (status is { } s)
        {
            conditions.Add("s.Status = @Status");
            parameters.Add("Status", (int)s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Replace("%", string.Empty).Replace("_", string.Empty).Replace("[", string.Empty).Trim();
            conditions.Add("(s.Reference LIKE @Pattern OR s.BusinessName LIKE @Pattern OR s.ContactName LIKE @Pattern OR s.Email LIKE @Pattern OR s.Phone LIKE @Pattern)");
            parameters.Add("Pattern", $"%{term}%");
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Skip", (page - 1) * pageSize);
        parameters.Add("Take", pageSize);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM StandingOrders s {where}", parameters, cancellationToken: cancellationToken));
        if (total == 0)
        {
            return PagedResult<StandingOrderSummaryDto>.Empty(page, pageSize);
        }

        // Awaiting approval first, then active, then the rest; newest first within each group.
        var sql = dialect.Page($"""
            SELECT {SummaryColumns} FROM StandingOrders s {where}
            ORDER BY CASE s.Status WHEN 0 THEN 0 WHEN 1 THEN 1 WHEN 2 THEN 2 ELSE 3 END, s.CreatedAtUtc DESC, s.Id DESC
            """);
        var items = (await connection.QueryAsync<StandingOrderSummaryDto>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).AsList();
        await AttachLineSummariesAsync(connection, items, cancellationToken);
        return new PagedResult<StandingOrderSummaryDto>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<StandingOrderSummaryDto>> GetForCustomerAsync(string customerId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = (await connection.QueryAsync<StandingOrderSummaryDto>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM StandingOrders s WHERE s.CustomerId = @CustomerId ORDER BY s.CreatedAtUtc DESC, s.Id DESC",
            new { CustomerId = customerId },
            cancellationToken: cancellationToken))).AsList();
        await AttachLineSummariesAsync(connection, items, cancellationToken);
        return items;
    }

    public async Task<int> CountByStatusAsync(StandingOrderStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM StandingOrders WHERE Status = @Status", new { Status = (int)status }, cancellationToken: cancellationToken));
    }

    // ---------------------------------------------------------------------------------------------
    // Occurrence ledger
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<OccurrenceRecord>> GetOccurrencesAsync(int standingOrderId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<OccurrenceRecord>(new CommandDefinition(
            """
            SELECT c.StandingOrderId, c.OccurrenceDate, c.Status, c.OrderId, o.OrderNumber, o.Status AS OrderStatus, c.Reason, c.CreatedAtUtc
            FROM StandingOrderOccurrences c
            LEFT JOIN Orders o ON o.Id = c.OrderId
            WHERE c.StandingOrderId = @StandingOrderId AND c.OccurrenceDate >= @From AND c.OccurrenceDate <= @To
            ORDER BY c.OccurrenceDate
            """,
            new { StandingOrderId = standingOrderId, From = ToDate(from), To = ToDate(to) },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<bool> TryAddOccurrenceAsync(int standingOrderId, DateOnly date, OccurrenceStatus status, string? reason, CancellationToken cancellationToken = default)
    {
        // Portable "insert if missing": the primary key (StandingOrderId, OccurrenceDate) guarantees only one writer wins.
        const string sql = """
            INSERT INTO StandingOrderOccurrences (StandingOrderId, OccurrenceDate, Status, OrderId, Reason, CreatedAtUtc)
            SELECT @StandingOrderId, @OccurrenceDate, @Status, NULL, @Reason, @CreatedAtUtc
            WHERE NOT EXISTS (SELECT 1 FROM StandingOrderOccurrences WHERE StandingOrderId = @StandingOrderId AND OccurrenceDate = @OccurrenceDate)
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var inserted = await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                StandingOrderId = standingOrderId,
                OccurrenceDate = ToDate(date),
                Status = (int)status,
                Reason = Truncate(reason),
                CreatedAtUtc = DateTime.UtcNow,
            }, cancellationToken: cancellationToken));
            return inserted > 0;
        }
        catch (DbException ex) when (IsDuplicateKey(ex))
        {
            return false;
        }
    }

    public async Task SetOccurrenceOrderAsync(int standingOrderId, DateOnly date, int orderId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE StandingOrderOccurrences SET OrderId = @OrderId WHERE StandingOrderId = @StandingOrderId AND OccurrenceDate = @OccurrenceDate",
            new { StandingOrderId = standingOrderId, OccurrenceDate = ToDate(date), OrderId = orderId },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateOccurrenceAsync(int standingOrderId, DateOnly date, OccurrenceStatus status, string? reason, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE StandingOrderOccurrences SET Status = @Status, Reason = @Reason WHERE StandingOrderId = @StandingOrderId AND OccurrenceDate = @OccurrenceDate",
            new { StandingOrderId = standingOrderId, OccurrenceDate = ToDate(date), Status = (int)status, Reason = Truncate(reason) },
            cancellationToken: cancellationToken));
    }

    public async Task DeleteOccurrenceAsync(int standingOrderId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM StandingOrderOccurrences WHERE StandingOrderId = @StandingOrderId AND OccurrenceDate = @OccurrenceDate",
            new { StandingOrderId = standingOrderId, OccurrenceDate = ToDate(date) },
            cancellationToken: cancellationToken));
    }

    // ---------------------------------------------------------------------------------------------

    private async Task<StandingOrder?> LoadAsync(string condition, object value, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var order = await connection.QuerySingleOrDefaultAsync<StandingOrder>(new CommandDefinition(
            $"SELECT {Columns} FROM StandingOrders WHERE {condition}", new { Value = value }, cancellationToken: cancellationToken));
        if (order is null)
        {
            return null;
        }

        var lines = await connection.QueryAsync<StandingOrderLine>(new CommandDefinition(
            "SELECT Id, StandingOrderId, MenuItemId, ItemName, ItemBengaliName, Unit, UnitPrice, Quantity FROM StandingOrderLines WHERE StandingOrderId = @Id ORDER BY Id",
            new { order.Id },
            cancellationToken: cancellationToken));
        var events = await connection.QueryAsync<StandingOrderEvent>(new CommandDefinition(
            "SELECT Id, StandingOrderId, Description, ChangedBy, ChangedAtUtc FROM StandingOrderEvents WHERE StandingOrderId = @Id ORDER BY ChangedAtUtc, Id",
            new { order.Id },
            cancellationToken: cancellationToken));

        order.Hydrate(lines, events);
        return order;
    }

    private static object Parameters(StandingOrder o) => new
    {
        o.Id,
        o.Reference,
        o.CustomerId,
        o.BusinessName,
        o.ContactName,
        o.Email,
        o.Phone,
        o.TaxPermitNumber,
        Fulfillment = (int)o.Fulfillment,
        o.AddressLine1,
        o.AddressLine2,
        o.City,
        o.State,
        o.PostalCode,
        DaysOfWeek = (int)o.DaysOfWeek,
        o.PreferredTimeMinutes,
        o.StartDate,
        o.EndDate,
        o.Notes,
        o.AdminNotes,
        Status = (int)o.Status,
        o.StatusReason,
        o.TaxExempt,
        o.DeliveryFee,
        o.CreatedAtUtc,
        o.UpdatedAtUtc,
        o.ApprovedAtUtc,
    };

    private async Task InsertLinesAsync(DbConnection connection, DbTransaction transaction, StandingOrder order, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO StandingOrderLines (StandingOrderId, MenuItemId, ItemName, ItemBengaliName, Unit, UnitPrice, Quantity)
            VALUES (@StandingOrderId, @MenuItemId, @ItemName, @ItemBengaliName, @Unit, @UnitPrice, @Quantity)
            """;

        foreach (var line in order.Lines)
        {
            line.AttachTo(order.Id);
            await connection.ExecuteAsync(new CommandDefinition(sql, line, transaction, cancellationToken: cancellationToken));
        }
    }

    private async Task InsertNewEventsAsync(DbConnection connection, DbTransaction transaction, StandingOrder order, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO StandingOrderEvents (StandingOrderId, Description, ChangedBy, ChangedAtUtc)
            VALUES (@StandingOrderId, @Description, @ChangedBy, @ChangedAtUtc)
            """;

        foreach (var e in order.Events.Where(e => e.IsTransient))
        {
            e.AttachTo(order.Id);
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(sql), e, transaction, cancellationToken: cancellationToken));
            e.AssignId(id);
        }
    }

    /// <summary>Adds the per-delivery subtotal and a "10 lb Aloo Vorta, 5 lb ..." preview to each row.</summary>
    private static async Task AttachLineSummariesAsync(DbConnection connection, List<StandingOrderSummaryDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var lines = await connection.QueryAsync<PreviewLine>(new CommandDefinition(
            "SELECT StandingOrderId, ItemName, Quantity, Unit, UnitPrice FROM StandingOrderLines WHERE StandingOrderId IN @Ids ORDER BY Id",
            new { Ids = items.Select(i => i.Id).ToArray() },
            cancellationToken: cancellationToken));

        var byOrder = lines.GroupBy(l => l.StandingOrderId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var item in items)
        {
            if (byOrder.TryGetValue(item.Id, out var orderLines))
            {
                item.ItemsPreview = string.Join(", ", orderLines.Select(l => $"{Format.Quantity(l.Quantity, l.Unit)} {l.ItemName}"));
                item.SubtotalPerDelivery = orderLines.Sum(l => Math.Round(l.UnitPrice * l.Quantity, 2, MidpointRounding.AwayFromZero));
            }
        }
    }

    private static DateTime ToDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private static string? Truncate(string? value) => value is { Length: > 500 } ? value[..500] : value;

    /// <summary>SQLite: UNIQUE/PRIMARY KEY constraint (19). SQL Server: duplicate key (2627, 2601).</summary>
    private static bool IsDuplicateKey(DbException ex) => ex switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19,
        SqlException sql => sql.Number is 2627 or 2601,
        _ => false,
    };

    private sealed class PreviewLine
    {
        public int StandingOrderId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public string Unit { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }
    }
}
