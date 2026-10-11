using ShutkiVorta.Application.Common.Interfaces;

namespace ShutkiVorta.Application.Common.Security;

/// <summary>Scoped: the access is loaded the first time it is needed in a request and reused for the rest of it.</summary>
internal sealed class CurrentStaff(ICurrentUser currentUser, IStaffAccessService staffAccess) : ICurrentStaff
{
    private StaffAccess? _access;

    public async ValueTask<StaffAccess> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_access is not null)
        {
            return _access;
        }

        var userId = currentUser.UserId;
        return _access = userId is null ? StaffAccess.None : await staffAccess.GetAsync(userId, cancellationToken);
    }
}
