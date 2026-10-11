using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Staff;

/// <summary>A staff member (or a former one) as listed on the Users page. Times are UTC.</summary>
public sealed class StaffMemberDto
{
    public string UserId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }

    /// <summary>The staff role; null for former staff (they have a staff profile but no staff role any more).</summary>
    public StaffRole? Role { get; init; }

    public string? JobTitle { get; init; }
    public bool UsesCustomPermissions { get; init; }

    /// <summary>False while an invitation is pending: the person has not set a password yet.</summary>
    public bool HasPassword { get; init; }

    /// <summary>Staff access needs a confirmed email; using the invitation link confirms it.</summary>
    public bool EmailConfirmed { get; init; }

    public bool IsLockedOut { get; init; }
    public DateTime? LockoutEndUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>When the person's open shift started, or null when they are not on the clock.</summary>
    public DateTime? OnClockSinceUtc { get; init; }

    /// <summary>The data holds more than one staff role for this person (the lowest counts); saving a role fixes it.</summary>
    public bool HasConflictingRoles { get; init; }

    public bool IsFormerStaff => Role is null;
    public bool IsInvitationPending => !HasPassword;

    /// <summary>Signed-in staff access: a staff role and a confirmed email.</summary>
    public bool HasStaffAccess => Role is not null && EmailConfirmed;
}

/// <param name="Role">Only this role (null: every role).</param>
/// <param name="Search">Part of the name, email or phone number.</param>
/// <param name="IncludeFormer">Also list former staff (a staff profile but no staff role).</param>
public sealed record StaffListFilter(StaffRole? Role = null, string? Search = null, bool IncludeFormer = false)
{
    public static StaffListFilter All { get; } = new();
}

/// <summary>A role's permissions as an admin set them on the Permissions page.</summary>
public sealed class RolePermissionSetDto
{
    public StaffRole Role { get; init; }

    /// <summary>Optimistic concurrency token: 0 until the role's permissions are first saved, then +1 on every save.</summary>
    public int Revision { get; init; }

    /// <summary>The stored choices (permission → granted); empty when the role follows the defaults.</summary>
    public IReadOnlyDictionary<string, bool> Overrides { get; init; } = new Dictionary<string, bool>();

    /// <summary>What the role has: the catalog defaults with the overrides applied.</summary>
    public IReadOnlySet<string> Granted { get; init; } = new HashSet<string>();

    public DateTime? UpdatedAtUtc { get; init; }
    public string? UpdatedBy { get; init; }

    public bool IsDefault => Granted.SetEquals(PermissionCatalog.Defaults(Role));
}

/// <summary>One person's own permissions, used instead of their role's when <see cref="UsesCustomPermissions"/> is set.</summary>
public sealed class CustomPermissionsDto
{
    public string UserId { get; init; } = string.Empty;
    public bool UsesCustomPermissions { get; init; }

    /// <summary>The person's own grants (empty while they follow their role).</summary>
    public IReadOnlySet<string> Granted { get; init; } = new HashSet<string>();

    /// <summary>StaffProfiles.Version: optimistic concurrency token for the person's profile and permissions (0 = no profile yet).</summary>
    public int Version { get; init; }
}
