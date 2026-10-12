namespace ShutkiVorta.Application.Features.Staff;

/// <summary>
/// Optimistic concurrency for the changes on a person's page that <see cref="IStaffPermissionStore"/> takes no version for
/// (their role, removal, name and phone): every form posts the StaffProfiles.Version it was shown, and the change only goes
/// ahead once it has claimed that version, so of two admins posting from the same page only the first gets through.
/// </summary>
public interface IStaffProfileVersions
{
    /// <summary>
    /// Moves the person's version on in one atomic step if it is still <paramref name="expectedVersion"/>; false when someone
    /// changed the person meanwhile. Someone without a staff profile yet is at version 0 (and gets one).
    /// </summary>
    Task<bool> ClaimAsync(string userId, int expectedVersion, string changedBy, CancellationToken cancellationToken = default);
}
