using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Features.Staff;

/// <summary>
/// Every change to who is staff and what they may do goes through here (one transaction each, with an audit entry), so
/// the rules hold whichever page makes the change. Changes apply on the person's next request; nothing is cached and
/// nobody is signed out. Callers check that the actor is an Admin (IRequireAdmin); this store enforces the data rules.
/// </summary>
public interface IStaffPermissionStore
{
    /// <summary>The role's permissions with their revision. Admin is not configurable.</summary>
    Task<RolePermissionSetDto> GetRolePermissionsAsync(StaffRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the full set of permissions for Manager or Employee (normalised; unknown keys dropped). Fails with
    /// "someone changed these permissions" when the stored revision is not <paramref name="expectedRevision"/>.
    /// </summary>
    Task<Result> SaveRolePermissionsAsync(StaffRole role, IReadOnlySet<string> granted, int expectedRevision, string changedBy, CancellationToken cancellationToken = default);

    /// <summary>Removes an admin's changes so the role follows the catalog defaults again (revision checked as above).</summary>
    Task<Result> RestoreRoleDefaultsAsync(StaffRole role, int expectedRevision, string changedBy, CancellationToken cancellationToken = default);

    Task<CustomPermissionsDto> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives a Manager or Employee their own permissions (normalised), or makes them follow their role again when
    /// <paramref name="granted"/> is null. Fails when the profile's version is not <paramref name="expectedVersion"/>.
    /// </summary>
    Task<Result> SetUserPermissionsAsync(string userId, IReadOnlySet<string>? granted, int expectedVersion, string changedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the person Admin, Manager or Employee, or removes them from the staff (<paramref name="role"/> null), in one
    /// transaction serialised with every other role change: refuses to change <paramref name="actorUserId"/>'s own role
    /// and to leave no Admin who can sign in (a password and a confirmed email); removes any other staff role; creates or
    /// updates the staff profile; resets custom permissions on any change; on removal closes an open shift
    /// (ClockOutSource.System, flagged for review); writes the audit entry. Setting the role someone already has changes
    /// nothing. Does not touch the Customer role, the password or the security stamp.
    /// </summary>
    Task<Result> SetStaffRoleAsync(string userId, StaffRole? role, string actorUserId, string changedBy, CancellationToken cancellationToken = default);

    /// <summary>Saves the job title (blank = none), creating the staff profile if needed.</summary>
    Task UpdateProfileAsync(string userId, string? jobTitle, string changedBy, CancellationToken cancellationToken = default);
}
