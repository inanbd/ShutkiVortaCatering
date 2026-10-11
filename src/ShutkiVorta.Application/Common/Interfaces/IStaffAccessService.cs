using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>
/// Reads anyone's staff access from the database (one round trip, no caching between requests, so role and permission
/// changes apply on the next request on every server). Prefer <see cref="ICurrentStaff"/> for the signed-in person;
/// use this when the person is not the request's user yet, e.g. right after signing in.
/// </summary>
public interface IStaffAccessService
{
    Task<StaffAccess> GetAsync(string userId, CancellationToken cancellationToken = default);
}
