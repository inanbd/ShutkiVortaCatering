namespace ShutkiVorta.Application.Common.Security;

/// <summary>The three kinds of staff. A lower value is a more powerful role.</summary>
public enum StaffRole
{
    Admin = 1,
    Manager = 2,
    Employee = 3,
}

public static class StaffRoles
{
    public static readonly IReadOnlyList<StaffRole> All = [StaffRole.Admin, StaffRole.Manager, StaffRole.Employee];

    /// <summary>The roles whose permissions an admin chooses (Admins always have every permission).</summary>
    public static readonly IReadOnlyList<StaffRole> Configurable = [StaffRole.Manager, StaffRole.Employee];

    /// <summary>The Identity role name, e.g. "Manager".</summary>
    public static string RoleName(this StaffRole role) => role switch
    {
        StaffRole.Admin => Roles.Admin,
        StaffRole.Manager => Roles.Manager,
        StaffRole.Employee => Roles.Employee,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown staff role."),
    };

    /// <summary>Identity's normalized role name, as stored in Roles.NormalizedName.</summary>
    public static string NormalizedName(this StaffRole role) => role.RoleName().ToUpperInvariant();

    public static string DisplayName(this StaffRole role) => role.RoleName();

    /// <summary>The staff role for an Identity role name (any case), or null for Customer and unknown names.</summary>
    public static StaffRole? FromRoleName(string? roleName) => roleName?.ToUpperInvariant() switch
    {
        "ADMIN" => StaffRole.Admin,
        "MANAGER" => StaffRole.Manager,
        "EMPLOYEE" => StaffRole.Employee,
        _ => null,
    };

    /// <summary>
    /// A person should hold one staff role; if the data ever holds several, the least powerful one counts (fail closed).
    /// </summary>
    public static StaffRole? Lowest(IEnumerable<StaffRole> roles) =>
        roles.Select(r => (StaffRole?)r).OrderByDescending(r => r).FirstOrDefault();
}
