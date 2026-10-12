using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Domain.Orders;
using ShutkiVorta.Infrastructure.Identity;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.IntegrationTests.Infrastructure;

/// <summary>An order written straight into the database, with exactly the owner, status, total and time a test needs.</summary>
public sealed record TestOrder(string Email, decimal Total, DateTime CreatedAtUtc)
{
    public string? CustomerId { get; init; }
    public string Name { get; init; } = "Test Customer";
    public string Phone { get; init; } = "214-555-0100";
    public OrderStatus Status { get; init; } = OrderStatus.Pending;

    /// <summary>A delivery order to this street address (Dallas, TX); null for pickup.</summary>
    public string? DeliveryStreet { get; init; }

    public int? StandingOrderId { get; init; }
}

/// <summary>Customers for the customer list tests: accounts, orders (with or without an account) and standing orders.</summary>
public static class CustomerTestData
{
    /// <summary>Creates an account (with the Customer role unless told otherwise) and returns its id.</summary>
    public static async Task<string> CreateAccountAsync(
        AppFactory app, string name, string email, bool confirmed = true, bool customerRole = true, DateTime? createdAtUtc = null,
        string? phone = null, string? street = null)
    {
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            FullName = name,
            PhoneNumber = phone,
            AddressLine1 = street,
            City = street is null ? null : "Dallas",
            State = street is null ? null : "TX",
            PostalCode = street is null ? null : "75201",
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
        };
        var created = await users.CreateAsync(user, AppFactory.AdminPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        if (customerRole)
        {
            Assert.True((await users.AddToRoleAsync(user, Roles.Customer)).Succeeded);
        }

        return user.Id;
    }

    /// <summary>Writes the order (email lower-cased, as checkout stores it) and returns its order number.</summary>
    public static async Task<string> AddOrderAsync(AppFactory app, TestOrder order)
    {
        var number = $"SV-T{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO Orders (OrderNumber, TrackingToken, CustomerId, CustomerName, Email, Phone, Fulfillment, AddressLine1, City, State,
                PostalCode, ScheduledFor, Status, Subtotal, DeliveryFee, Tax, Total, CreatedAtUtc, UpdatedAtUtc, StandingOrderId, CompanyName)
            VALUES (@Number, @Token, @CustomerId, @Name, @Email, @Phone, @Fulfillment, @Street, @City, @State,
                @PostalCode, @ScheduledFor, @Status, @Total, 0, 0, @Total, @CreatedAtUtc, @CreatedAtUtc, @StandingOrderId, @CompanyName)
            """,
            new
            {
                Number = number,
                Token = Guid.NewGuid().ToString("N"),
                order.CustomerId,
                order.Name,
                Email = order.Email.ToLowerInvariant(),
                order.Phone,
                Fulfillment = (int)(order.DeliveryStreet is null ? FulfillmentMethod.Pickup : FulfillmentMethod.Delivery),
                Street = order.DeliveryStreet,
                City = order.DeliveryStreet is null ? null : "Dallas",
                State = order.DeliveryStreet is null ? null : "TX",
                PostalCode = order.DeliveryStreet is null ? null : "75202",
                ScheduledFor = order.CreatedAtUtc.AddDays(2),
                Status = (int)order.Status,
                order.Total,
                order.CreatedAtUtc,
                order.StandingOrderId,
                CompanyName = order.StandingOrderId is null ? null : "Restaurant",
            });
        return number;
    }

    /// <summary>A standing order (awaiting approval) of the account; returns its id.</summary>
    public static async Task<int> AddStandingOrderAsync(AppFactory app, string customerId, string businessName, string email)
    {
        var dialect = app.Services.GetRequiredService<ISqlDialect>();
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        return await connection.ExecuteScalarAsync<int>(
            dialect.InsertReturningId(
                """
                INSERT INTO StandingOrders (Reference, CustomerId, BusinessName, ContactName, Email, Phone, Fulfillment, DaysOfWeek,
                    PreferredTimeMinutes, StartDate, Status, TaxExempt, DeliveryFee, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@Reference, @CustomerId, @BusinessName, 'Owner', @Email, '214-555-0199', 1, 2, 600, @StartDate, 0, 0, 0, @Now, @Now)
                """),
            new
            {
                Reference = $"R-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
                CustomerId = customerId,
                BusinessName = businessName,
                Email = email.ToLowerInvariant(),
                StartDate = new DateTime(2026, 11, 2),
                Now = DateTime.UtcNow,
            });
    }

    /// <summary>An inquiry sent with this email (stored lower-cased, as the contact form does).</summary>
    public static async Task AddInquiryAsync(AppFactory app, string name, string email, string message, DateTime createdAtUtc)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO CateringInquiries (Topic, Name, Email, Message, IsHandled, CreatedAtUtc) VALUES (1, @Name, @Email, @Message, 0, @CreatedAtUtc)",
            new { Name = name, Email = email.ToLowerInvariant(), Message = message, CreatedAtUtc = createdAtUtc });
    }
}
