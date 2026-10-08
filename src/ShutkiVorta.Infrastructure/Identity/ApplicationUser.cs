namespace ShutkiVorta.Infrastructure.Identity;

/// <summary>Identity user persisted with Dapper (no Entity Framework). Doubles as the customer profile.</summary>
public sealed class ApplicationUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public string? PasswordHash { get; set; }
    public string? SecurityStamp { get; set; }
    public string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberConfirmed { get; set; }
    public bool LockoutEnabled { get; set; } = true;
    public DateTime? LockoutEndUtc { get; set; }
    public int AccessFailedCount { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ApplicationRole
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
}
