using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.UnitTests.TestDoubles;

/// <summary>Staff access per user id, set by the test; unknown users have none.</summary>
internal sealed class FakeStaffAccessService : IStaffAccessService
{
    public Dictionary<string, StaffAccess> Users { get; } = new(StringComparer.Ordinal);

    /// <summary>How many times access was read (to check per-request memoisation).</summary>
    public int Reads { get; private set; }

    public Task<StaffAccess> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(Users.TryGetValue(userId, out var access) ? access : StaffAccess.None);
    }
}
