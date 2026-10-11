using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>
/// The signed-in person's staff access, read from the database once per request (scope) and then remembered.
/// Anonymous visitors and background jobs get <see cref="StaffAccess.None"/>.
/// </summary>
public interface ICurrentStaff
{
    ValueTask<StaffAccess> GetAsync(CancellationToken cancellationToken = default);
}
