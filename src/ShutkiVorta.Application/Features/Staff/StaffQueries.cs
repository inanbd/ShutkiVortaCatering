using MediatR;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.TimeClock;

namespace ShutkiVorta.Application.Features.Staff;

/// <summary>
/// The staff list: current staff (optionally one role, or matching a search) or former staff. Staff with staff.view may
/// read it; only Admins change anything.
/// </summary>
[RequirePermission(Permissions.StaffView)]
public sealed record GetStaffListQuery(StaffRole? Role = null, string? Search = null, bool Former = false) : IRequest<StaffListDto>;

/// <summary>One staff member (or former one) with what they may do; null (404) for an account that was never staff.</summary>
[RequirePermission(Permissions.StaffView)]
public sealed record GetStaffMemberQuery(string UserId) : IRequest<StaffMemberDetailsDto?>;

/// <summary>The newest invitation or "you've been added" email to a staff member, for the Users page to show its delivery.</summary>
public sealed record GetStaffEmailStatusQuery(string UserId) : IRequest<StaffEmailStatusDto?>, IRequireAdmin;

/// <summary>The Permissions page: Manager and Employee permissions, who has custom permissions, and recent changes.</summary>
public sealed record GetPermissionsOverviewQuery : IRequest<PermissionsOverviewDto>, IRequireAdmin;

public sealed class StaffListDto
{
    /// <summary>The people asked for: current staff (filtered) or former staff.</summary>
    public IReadOnlyList<StaffMemberDto> Members { get; init; } = [];

    /// <summary>Current staff matching the search, whatever the role filter (for the tab counts).</summary>
    public int StaffCount { get; init; }

    public int FormerCount { get; init; }
}

public sealed class StaffMemberDetailsDto
{
    public StaffMemberDto Member { get; init; } = new();

    /// <summary>StaffProfiles.Version: posted back by every form on the person's page, so a change made meanwhile is noticed.</summary>
    public int Version { get; init; }

    /// <summary>
    /// What the person may do: everything for Admins, their own permissions when custom, otherwise their role's. Invited
    /// people get these once they have set their password.
    /// </summary>
    public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>();

    /// <summary>What their role has (Managers and Employees), for "Follow the role" and to compare with custom permissions.</summary>
    public IReadOnlySet<string> RolePermissions { get; init; } = new HashSet<string>();

    /// <summary>When their open shift started (UTC), or null when they are not on the clock.</summary>
    public DateTime? OnClockSinceUtc { get; init; }

    public IReadOnlyList<StaffAuditEntryDto> RecentActivity { get; init; } = [];

    /// <summary>The newest invitation or "you've been added" email to them (null for former staff).</summary>
    public StaffEmailStatusDto? LatestEmail { get; init; }
}

public sealed class PermissionsOverviewDto
{
    /// <summary>Manager, then Employee.</summary>
    public IReadOnlyList<RolePermissionSetDto> Roles { get; init; } = [];

    /// <summary>Managers and Employees with custom permissions: changes to their role's permissions do not reach them.</summary>
    public IReadOnlyList<StaffMemberDto> CustomPermissionPeople { get; init; } = [];

    public IReadOnlyList<StaffAuditEntryDto> RecentChanges { get; init; } = [];
}

internal sealed class StaffQueryHandlers(
    IStaffDirectory directory,
    IStaffPermissionStore store,
    IStaffAuditLog audit,
    IStaffEmailLog emails,
    ITimeClockStatusReader timeClock) :
    IRequestHandler<GetStaffListQuery, StaffListDto>,
    IRequestHandler<GetStaffMemberQuery, StaffMemberDetailsDto?>,
    IRequestHandler<GetStaffEmailStatusQuery, StaffEmailStatusDto?>,
    IRequestHandler<GetPermissionsOverviewQuery, PermissionsOverviewDto>
{
    public const int ActivityShown = 15;
    public const int RecentChangesShown = 15;

    private static readonly string[] StaffEmailSubjects = [StaffInvitations.InvitationSubject, StaffInvitations.AddedSubject];

    private static readonly string[] PermissionActions = [StaffAuditActions.RoleDefaultsChanged, StaffAuditActions.PermissionsChanged];

    public async Task<StaffListDto> Handle(GetStaffListQuery request, CancellationToken cancellationToken)
    {
        // One read: the staff list is small, and the tabs need both counts.
        var everyone = await directory.ListAsync(new StaffListFilter(Search: Clean(request.Search), IncludeFormer: true), cancellationToken);
        var current = everyone.Where(m => !m.IsFormerStaff).ToList();
        var former = everyone.Where(m => m.IsFormerStaff).ToList();

        return new StaffListDto
        {
            Members = request.Former ? former : request.Role is { } role ? current.Where(m => m.Role == role).ToList() : current,
            StaffCount = current.Count,
            FormerCount = former.Count,
        };
    }

    public async Task<StaffMemberDetailsDto?> Handle(GetStaffMemberQuery request, CancellationToken cancellationToken)
    {
        var member = await directory.GetAsync(request.UserId.Trim(), cancellationToken);
        if (member is null)
        {
            return null;
        }

        var custom = await store.GetUserPermissionsAsync(member.UserId, cancellationToken);
        IReadOnlySet<string> rolePermissions = member.Role switch
        {
            null => new HashSet<string>(),
            StaffRole.Admin => PermissionCatalog.Defaults(StaffRole.Admin),
            var role => (await store.GetRolePermissionsAsync(role.Value, cancellationToken)).Granted,
        };

        return new StaffMemberDetailsDto
        {
            Member = member,
            Version = custom.Version,
            Permissions = (member.Role is StaffRole.Manager or StaffRole.Employee) && custom.UsesCustomPermissions ? custom.Granted : rolePermissions,
            RolePermissions = rolePermissions,
            OnClockSinceUtc = await timeClock.GetOpenShiftStartAsync(member.UserId, cancellationToken),
            RecentActivity = await audit.ListAsync(member.UserId, ActivityShown, cancellationToken),
            LatestEmail = member.IsFormerStaff ? null : await emails.GetLatestAsync(member.Email, StaffEmailSubjects, cancellationToken),
        };
    }

    public async Task<StaffEmailStatusDto?> Handle(GetStaffEmailStatusQuery request, CancellationToken cancellationToken)
    {
        var member = await directory.GetAsync(request.UserId.Trim(), cancellationToken);
        return member is null ? null : await emails.GetLatestAsync(member.Email, StaffEmailSubjects, cancellationToken);
    }

    public async Task<PermissionsOverviewDto> Handle(GetPermissionsOverviewQuery request, CancellationToken cancellationToken)
    {
        var roles = new List<RolePermissionSetDto>();
        foreach (var role in StaffRoles.Configurable)
        {
            roles.Add(await store.GetRolePermissionsAsync(role, cancellationToken));
        }

        var staff = await directory.ListAsync(StaffListFilter.All, cancellationToken);
        return new PermissionsOverviewDto
        {
            Roles = roles,
            CustomPermissionPeople = staff.Where(m => m.UsesCustomPermissions && (m.Role is StaffRole.Manager or StaffRole.Employee)).ToList(),
            RecentChanges = await audit.ListByActionsAsync(PermissionActions, RecentChangesShown, cancellationToken),
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
