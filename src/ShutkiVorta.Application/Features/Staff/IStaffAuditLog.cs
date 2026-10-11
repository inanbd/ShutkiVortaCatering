namespace ShutkiVorta.Application.Features.Staff;

/// <summary>
/// Who changed what about the staff and their shifts. Entries keep the person's name, so they outlive deleted accounts.
/// </summary>
public interface IStaffAuditLog
{
    Task WriteAsync(StaffAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>The newest entries first; about one person, or about everyone when <paramref name="targetUserId"/> is null.</summary>
    Task<IReadOnlyList<StaffAuditEntryDto>> ListAsync(string? targetUserId, int take, CancellationToken cancellationToken = default);

    /// <summary>The newest entries with one of the <see cref="StaffAuditActions"/> given, e.g. permission changes.</summary>
    Task<IReadOnlyList<StaffAuditEntryDto>> ListByActionsAsync(IReadOnlyCollection<string> actions, int take, CancellationToken cancellationToken = default);
}

/// <param name="Action">One of <see cref="StaffAuditActions"/>.</param>
/// <param name="Details">A sentence for people, e.g. "Employee → Manager. Custom permissions were reset."</param>
/// <param name="ChangedBy">Who made the change (their email), or "System".</param>
public sealed record StaffAuditEntry(string? TargetUserId, string? TargetName, string Action, string? Details, string ChangedBy);

public sealed class StaffAuditEntryDto
{
    public long Id { get; init; }
    public string? TargetUserId { get; init; }
    public string? TargetName { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? Details { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public DateTime ChangedAtUtc { get; init; }
}

public static class StaffAuditActions
{
    public const string RoleChanged = "RoleChanged";
    public const string StaffAdded = "StaffAdded";
    public const string StaffRemoved = "StaffRemoved";
    public const string PermissionsChanged = "PermissionsChanged";
    public const string RoleDefaultsChanged = "RoleDefaultsChanged";
    public const string InvitationSent = "InvitationSent";
    public const string AccountUnlocked = "AccountUnlocked";
    public const string EmailChanged = "EmailChanged";
    public const string ProfileChanged = "ProfileChanged";
    public const string ShiftAdded = "ShiftAdded";
    public const string ShiftCorrected = "ShiftCorrected";
    public const string ShiftVoided = "ShiftVoided";
    public const string ShiftClosed = "ShiftClosed";
    public const string ShiftSelfCorrected = "ShiftSelfCorrected";

    /// <summary>A short label for people, e.g. "Role changed".</summary>
    public static string DisplayName(string action) => action switch
    {
        RoleChanged => "Role changed",
        StaffAdded => "Added to staff",
        StaffRemoved => "Removed from staff",
        PermissionsChanged => "Permissions changed",
        RoleDefaultsChanged => "Role permissions changed",
        InvitationSent => "Invitation sent",
        AccountUnlocked => "Sign-in unlocked",
        EmailChanged => "Email changed",
        ProfileChanged => "Details changed",
        ShiftAdded => "Shift added",
        ShiftCorrected => "Shift corrected",
        ShiftVoided => "Shift voided",
        ShiftClosed => "Shift closed",
        ShiftSelfCorrected => "Forgotten clock-out entered",
        _ => action,
    };
}
