using System.Globalization;
using Dapper;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Customers;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

/// <summary>
/// The customer list and pages. Every order lands on exactly one customer because every query uses the same rule: the
/// account that placed it, else the account whose CONFIRMED email it was placed with, else the guest with that email.
/// Emails are matched as <c>Users.NormalizedEmail = UPPER(o.Email)</c> everywhere: Users.Email keeps the casing people typed,
/// SQLite's <c>=</c> is case-sensitive and SQL Server's usually is not. (SQLite's UPPER only knows ASCII, so a non-ASCII
/// email can stay a guest on SQLite where SQL Server links it.)
/// </summary>
internal sealed class CustomerRepository(IDbConnectionFactory connections, ISqlDialect dialect) : ICustomerRepository
{
    private const string HasCustomerRole = """
        EXISTS (SELECT 1 FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId WHERE ur.UserId = u.Id AND r.NormalizedName = 'CUSTOMER')
        """;

    private const string HasStaffRole = """
        EXISTS (SELECT 1 FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
                 WHERE ur.UserId = u.Id AND r.NormalizedName IN ('ADMIN', 'MANAGER', 'EMPLOYEE'))
        """;

    private const string HasStandingOrder = "EXISTS (SELECT 1 FROM StandingOrders so WHERE so.CustomerId = u.Id)";

    // Accounts that are customers (the Customer role or at least one order), then guests: orders without an account whose
    // email no confirmed account has, grouped by that email, with the name and phone of their newest order. Cancelled
    // orders count for nothing but still make someone a customer. Orders count from when they are placed (CreatedAtUtc),
    // so a restaurant's upcoming deliveries count once they are generated, like any order for a later date. Filters go on
    // the OUTER query only, so a search picks rows without changing their figures.
    private const string Customers = $"""
        SELECT 'A' AS Kind, u.Id AS AccountId, NULL AS GuestEmail, u.FullName AS Name, u.Email AS Email, u.PhoneNumber AS Phone,
               (SELECT so.BusinessName FROM StandingOrders so
                 WHERE so.Id = (SELECT MAX(so2.Id) FROM StandingOrders so2 WHERE so2.CustomerId = u.Id)) AS BusinessName,
               u.CreatedAtUtc AS JoinedUtc, COALESCE(s.OrderCount, 0) AS OrderCount, COALESCE(s.OrderValue, 0) AS OrderValue,
               s.LastOrderUtc AS LastOrderUtc, NULL AS GuestOrderNumber,
               CASE WHEN {HasStandingOrder} THEN 1 ELSE 0 END AS IsRestaurant
        FROM Users u
        LEFT JOIN (SELECT k.AccountId,
                          SUM(CASE WHEN k.Status <> @Cancelled THEN 1 ELSE 0 END) AS OrderCount,
                          SUM(CASE WHEN k.Status <> @Cancelled THEN k.Total ELSE 0 END) AS OrderValue,
                          MAX(CASE WHEN k.Status <> @Cancelled THEN k.CreatedAtUtc END) AS LastOrderUtc
                     FROM (SELECT o.CustomerId AS AccountId, o.Status, o.Total, o.CreatedAtUtc FROM Orders o WHERE o.CustomerId IS NOT NULL
                           UNION ALL
                           SELECT cu.Id, o.Status, o.Total, o.CreatedAtUtc FROM Orders o
                           INNER JOIN Users cu ON cu.NormalizedEmail = UPPER(o.Email) AND cu.EmailConfirmed = 1
                           WHERE o.CustomerId IS NULL) k
                    GROUP BY k.AccountId) s ON s.AccountId = u.Id
        WHERE s.AccountId IS NOT NULL OR {HasCustomerRole}
        UNION ALL
        SELECT 'G', NULL, g.Email, lo.CustomerName, g.Email, lo.Phone, NULL, g.FirstOrderUtc, g.OrderCount, g.OrderValue,
               g.LastOrderUtc, lo.OrderNumber, 0
        FROM (SELECT o.Email, MIN(o.CreatedAtUtc) AS FirstOrderUtc, MAX(o.Id) AS LastOrderId,
                     SUM(CASE WHEN o.Status <> @Cancelled THEN 1 ELSE 0 END) AS OrderCount,
                     SUM(CASE WHEN o.Status <> @Cancelled THEN o.Total ELSE 0 END) AS OrderValue,
                     MAX(CASE WHEN o.Status <> @Cancelled THEN o.CreatedAtUtc END) AS LastOrderUtc
                FROM Orders o
               WHERE o.CustomerId IS NULL
                 AND NOT EXISTS (SELECT 1 FROM Users cu WHERE cu.NormalizedEmail = UPPER(o.Email) AND cu.EmailConfirmed = 1)
               GROUP BY o.Email) g
        INNER JOIN Orders lo ON lo.Id = g.LastOrderId
        """;

    private const string ListColumns = """
        c.Kind, c.AccountId, c.GuestEmail, c.Name, c.Email, c.Phone, c.BusinessName, c.JoinedUtc, c.OrderCount, c.OrderValue,
        c.LastOrderUtc, c.GuestOrderNumber, c.IsRestaurant
        """;

    // The orders counted on one account (@AccountId) or one guest (@Email), as in the list above.
    private const string AccountOrders = """
        (o.CustomerId = @AccountId
         OR (o.CustomerId IS NULL
             AND EXISTS (SELECT 1 FROM Users cu WHERE cu.Id = @AccountId AND cu.EmailConfirmed = 1 AND cu.NormalizedEmail = UPPER(o.Email))))
        """;

    private const string GuestOrders = """
        (o.CustomerId IS NULL AND o.Email = @Email
         AND NOT EXISTS (SELECT 1 FROM Users cu WHERE cu.NormalizedEmail = UPPER(o.Email) AND cu.EmailConfirmed = 1))
        """;

    private const string FiguresColumns = """
        COUNT(*) AS AllOrderCount,
        COALESCE(SUM(CASE WHEN o.Status <> @Cancelled THEN 1 ELSE 0 END), 0) AS OrderCount,
        COALESCE(SUM(CASE WHEN o.Status <> @Cancelled THEN o.Total ELSE 0 END), 0) AS OrderValue,
        MIN(CASE WHEN o.Status <> @Cancelled THEN o.CreatedAtUtc END) AS FirstOrderUtc,
        MAX(CASE WHEN o.Status <> @Cancelled THEN o.CreatedAtUtc END) AS LastOrderUtc,
        MAX(o.Id) AS LastOrderId
        """;

    public async Task<CustomerListDto> SearchAsync(CustomerSearch search, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Cancelled", (int)OrderStatus.Cancelled);

        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(search.Search))
        {
            // Wildcards are escaped, not dropped: people search by email, and emails often have an underscore.
            conditions.Add("""
                (c.Name LIKE @Pattern ESCAPE '\' OR c.Email LIKE @Pattern ESCAPE '\' OR c.Phone LIKE @Pattern ESCAPE '\'
                 OR c.BusinessName LIKE @Pattern ESCAPE '\')
                """);
            parameters.Add("Pattern", SqlLike.Contains(search.Search));
        }

        await using var connection = await connections.OpenAsync(cancellationToken);

        // How many of each type match the search: the tabs, and the total of the type asked for.
        var counts = (await connection.QueryAsync<TypeCountRow>(new CommandDefinition(
            $"SELECT c.Kind, c.IsRestaurant, COUNT(*) AS Total FROM ({Customers}) c {Where(conditions)} GROUP BY c.Kind, c.IsRestaurant",
            parameters,
            cancellationToken: cancellationToken))).ToList();
        int CountOf(CustomerType type) => counts.Where(c => ToType(c.Kind, c.IsRestaurant) == type).Sum(c => c.Total);

        var accounts = CountOf(CustomerType.Account);
        var guests = CountOf(CustomerType.Guest);
        var restaurants = CountOf(CustomerType.Restaurant);
        var total = search.Type is { } only ? CountOf(only) : accounts + guests + restaurants;

        // A page past the end (an old link, or customers who no longer match) shows the last page instead.
        var pageNumber = Paging.Within(search.Page, total, search.PageSize);
        var page = PagedResult<CustomerListItemDto>.Empty(pageNumber, search.PageSize);
        if (total > 0)
        {
            switch (search.Type)
            {
                case CustomerType.Account:
                    conditions.Add("c.Kind = 'A' AND c.IsRestaurant = 0");
                    break;
                case CustomerType.Guest:
                    conditions.Add("c.Kind = 'G'");
                    break;
                case CustomerType.Restaurant:
                    conditions.Add("c.IsRestaurant = 1");
                    break;
            }

            // Every sort ends with the row's unique key (account id, or a guest's email), so pages never repeat or skip rows.
            var order = search.Sort switch
            {
                CustomerSort.OrderValue => "c.OrderValue DESC, c.LastOrderUtc DESC",
                CustomerSort.Name => "UPPER(c.Name)",
                CustomerSort.Newest => "c.JoinedUtc DESC",
                _ => "c.LastOrderUtc DESC",
            };

            parameters.Add("Skip", Paging.Skip(pageNumber, search.PageSize));
            parameters.Add("Take", search.PageSize);
            var rows = await connection.QueryAsync<CustomerRow>(new CommandDefinition(
                dialect.Page($"SELECT {ListColumns} FROM ({Customers}) c {Where(conditions)} ORDER BY {order}, c.AccountId, c.GuestEmail"),
                parameters,
                cancellationToken: cancellationToken));
            page = new PagedResult<CustomerListItemDto>(rows.Select(r => r.ToDto()).ToList(), total, pageNumber, search.PageSize);
        }

        return new CustomerListDto { Customers = page, AccountCount = accounts, GuestCount = guests, RestaurantCount = restaurants };
    }

    public async Task<CustomerSummaryDto?> GetAccountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var sql = $"""
            SELECT u.Id, u.FullName, u.Email, u.PhoneNumber, u.EmailConfirmed, u.CreatedAtUtc,
                   u.AddressLine1, u.AddressLine2, u.City, u.State, u.PostalCode,
                   CASE WHEN {HasCustomerRole} THEN 1 ELSE 0 END AS HasCustomerRole,
                   CASE WHEN {HasStaffRole} THEN 1 ELSE 0 END AS IsStaff,
                   CASE WHEN {HasStandingOrder} THEN 1 ELSE 0 END AS IsRestaurant
            FROM Users u WHERE u.Id = @AccountId;
            SELECT {FiguresColumns} FROM Orders o WHERE {AccountOrders};
            {LatestDeliverySql(AccountOrders)};
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql, Parameters(new { AccountId = userId }), cancellationToken: cancellationToken));
        var account = await results.ReadSingleOrDefaultAsync<AccountRow>();
        var figures = await results.ReadSingleAsync<FiguresRow>();
        var delivery = await results.ReadSingleOrDefaultAsync<AddressRow>();

        // Staff-only accounts (no Customer role, no orders) are not customers.
        if (account is null || (account.HasCustomerRole == 0 && figures.AllOrderCount == 0))
        {
            return null;
        }

        return new CustomerSummaryDto
        {
            Type = account.IsRestaurant != 0 ? CustomerType.Restaurant : CustomerType.Account,
            AccountId = account.Id,
            Name = account.FullName,
            Email = account.Email,
            Phone = account.PhoneNumber,
            EmailConfirmed = account.EmailConfirmed,
            IsStaff = account.IsStaff != 0,
            JoinedUtc = Utc(account.CreatedAtUtc),
            SavedAddress = OneLine(account.AddressLine1, account.AddressLine2, account.City, account.State, account.PostalCode),
            LatestDeliveryAddress = delivery?.ToText(),
            LatestDeliveryUtc = delivery is null ? null : Utc(delivery.CreatedAtUtc),
            AllOrderCount = figures.AllOrderCount,
            OrderCount = figures.OrderCount,
            OrderValue = Math.Round(figures.OrderValue, 2),
            FirstOrderUtc = Utc(figures.FirstOrderUtc),
            LastOrderUtc = Utc(figures.LastOrderUtc),
        };
    }

    public async Task<CustomerSummaryDto?> GetGuestAsync(string email, CancellationToken cancellationToken = default)
    {
        // The name and phone come from the guest's newest order; an account with the same, unconfirmed email is mentioned
        // when it is a customer itself (the guest's orders are not counted on it).
        var sql = $"""
            SELECT f.AllOrderCount, f.OrderCount, f.OrderValue, f.FirstOrderUtc, f.LastOrderUtc, f.LastOrderId,
                   lo.CustomerName AS Name, lo.Email, lo.Phone
            FROM (SELECT {FiguresColumns} FROM Orders o WHERE {GuestOrders}) f
            INNER JOIN Orders lo ON lo.Id = f.LastOrderId;
            SELECT u.Id FROM Users u
             WHERE u.NormalizedEmail = UPPER(@Email) AND u.EmailConfirmed = 0
               AND (EXISTS (SELECT 1 FROM Orders ao WHERE ao.CustomerId = u.Id) OR {HasCustomerRole});
            {LatestDeliverySql(GuestOrders)};
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var results = await connection.QueryMultipleAsync(new CommandDefinition(
            sql, Parameters(new { Email = email }), cancellationToken: cancellationToken));
        var guest = await results.ReadSingleOrDefaultAsync<GuestRow>();
        var unconfirmedAccountId = await results.ReadSingleOrDefaultAsync<string>();
        var delivery = await results.ReadSingleOrDefaultAsync<AddressRow>();
        if (guest is null)
        {
            return null;
        }

        return new CustomerSummaryDto
        {
            Type = CustomerType.Guest,
            Name = guest.Name,
            Email = guest.Email,
            Phone = guest.Phone,
            LatestDeliveryAddress = delivery?.ToText(),
            LatestDeliveryUtc = delivery is null ? null : Utc(delivery.CreatedAtUtc),
            AllOrderCount = guest.AllOrderCount,
            OrderCount = guest.OrderCount,
            OrderValue = Math.Round(guest.OrderValue, 2),
            FirstOrderUtc = Utc(guest.FirstOrderUtc),
            LastOrderUtc = Utc(guest.LastOrderUtc),
            UnconfirmedAccountId = unconfirmedAccountId,
        };
    }

    public async Task<OrderOwnerDto?> GetOrderOwnerAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<OrderOwnerDto>(new CommandDefinition(
            """
            SELECT o.Email,
                   COALESCE(o.CustomerId,
                            (SELECT cu.Id FROM Users cu WHERE cu.NormalizedEmail = UPPER(o.Email) AND cu.EmailConfirmed = 1)) AS AccountId
            FROM Orders o WHERE o.OrderNumber = @OrderNumber
            """,
            new { OrderNumber = orderNumber },
            cancellationToken: cancellationToken));
    }

    public Task<IReadOnlyList<OrderSummaryDto>> GetAccountOrdersAsync(string userId, int take, CancellationToken cancellationToken = default) =>
        GetOrdersAsync(AccountOrders, new { AccountId = userId, Skip = 0, Take = take }, cancellationToken);

    public Task<IReadOnlyList<OrderSummaryDto>> GetGuestOrdersAsync(string email, int take, CancellationToken cancellationToken = default) =>
        GetOrdersAsync(GuestOrders, new { Email = email, Skip = 0, Take = take }, cancellationToken);

    private async Task<IReadOnlyList<OrderSummaryDto>> GetOrdersAsync(string predicate, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var items = (await connection.QueryAsync<OrderSummaryDto>(new CommandDefinition(
            dialect.Page($"SELECT {OrderRepository.SummaryColumns} FROM Orders o WHERE {predicate} ORDER BY o.CreatedAtUtc DESC, o.Id DESC"),
            parameters,
            cancellationToken: cancellationToken))).AsList();
        await OrderRepository.AttachPreviewsAsync(connection, items, cancellationToken);
        return items;
    }

    /// <summary>The newest delivery address among the orders matching <paramref name="predicate"/> (uses @Skip and @Take).</summary>
    private string LatestDeliverySql(string predicate) => dialect.Page($"""
        SELECT o.AddressLine1, o.AddressLine2, o.City, o.State, o.PostalCode, o.CreatedAtUtc
        FROM Orders o
        WHERE {predicate} AND o.Fulfillment = @Delivery AND o.AddressLine1 IS NOT NULL
        ORDER BY o.CreatedAtUtc DESC, o.Id DESC
        """);

    private static DynamicParameters Parameters(object key)
    {
        var parameters = new DynamicParameters(key);
        parameters.Add("Cancelled", (int)OrderStatus.Cancelled);
        parameters.Add("Delivery", (int)FulfillmentMethod.Delivery);
        parameters.Add("Skip", 0);
        parameters.Add("Take", 1);
        return parameters;
    }

    private static string Where(IReadOnlyCollection<string> conditions) =>
        conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);

    private static CustomerType ToType(string kind, int isRestaurant) =>
        kind == "G" ? CustomerType.Guest : isRestaurant != 0 ? CustomerType.Restaurant : CustomerType.Account;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    /// <summary>A date read as an object: DateTime on SQL Server, text on SQLite, or null.</summary>
    private static DateTime? Utc(object? value) => value switch
    {
        null => null,
        DateTime date => Utc(date),
        string text => Utc(DateTime.Parse(text, CultureInfo.InvariantCulture)),
        _ => throw new InvalidCastException($"Expected a date, got {value.GetType().Name}."),
    };

    private static string? OneLine(string? line1, string? line2, string? city, string? state, string? postalCode)
    {
        var text = string.Join(", ", new[] { line1, line2, city, $"{state} {postalCode}".Trim() }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }

    private sealed class TypeCountRow
    {
        public string Kind { get; init; } = string.Empty;
        public int IsRestaurant { get; init; }
        public int Total { get; init; }
    }

    private sealed class CustomerRow
    {
        public string Kind { get; init; } = string.Empty;
        public string? AccountId { get; init; }
        public string? GuestEmail { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string? BusinessName { get; init; }
        public DateTime JoinedUtc { get; init; }
        public int OrderCount { get; init; }
        public decimal OrderValue { get; init; }

        /// <summary>
        /// Read as it comes: SQLite returns dates as text, and when the first row's value is NULL it gives the computed column
        /// no type, so Dapper would cast the other rows' text to DateTime? and fail.
        /// </summary>
        public object? LastOrderUtc { get; init; }

        public string? GuestOrderNumber { get; init; }
        public int IsRestaurant { get; init; }

        public CustomerListItemDto ToDto() => new()
        {
            Type = ToType(Kind, IsRestaurant),
            AccountId = AccountId,
            Name = Name,
            Email = Email,
            Phone = Phone,
            BusinessName = BusinessName,
            OrderCount = OrderCount,
            OrderValue = Math.Round(OrderValue, 2),
            LastOrderUtc = Utc(LastOrderUtc),
            JoinedUtc = Utc(JoinedUtc),
            GuestOrderNumber = GuestOrderNumber,
        };
    }

    private sealed class AccountRow
    {
        public string Id { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? PhoneNumber { get; init; }
        public bool EmailConfirmed { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public string? AddressLine1 { get; init; }
        public string? AddressLine2 { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? PostalCode { get; init; }
        public int HasCustomerRole { get; init; }
        public int IsStaff { get; init; }
        public int IsRestaurant { get; init; }
    }

    private class FiguresRow
    {
        public int AllOrderCount { get; init; }
        public int OrderCount { get; init; }
        public decimal OrderValue { get; init; }
        public DateTime? FirstOrderUtc { get; init; }
        public DateTime? LastOrderUtc { get; init; }
        public int? LastOrderId { get; init; }
    }

    private sealed class GuestRow : FiguresRow
    {
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? Phone { get; init; }
    }

    private sealed class AddressRow
    {
        public string? AddressLine1 { get; init; }
        public string? AddressLine2 { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? PostalCode { get; init; }
        public DateTime CreatedAtUtc { get; init; }

        public string? ToText() => OneLine(AddressLine1, AddressLine2, City, State, PostalCode);
    }
}
