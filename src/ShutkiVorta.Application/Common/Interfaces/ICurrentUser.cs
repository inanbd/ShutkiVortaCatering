namespace ShutkiVorta.Application.Common.Interfaces;

/// <summary>
/// The signed-in user as the auth cookie describes them. Not for authorization decisions about staff: use
/// <see cref="ICurrentStaff"/>, which reads roles and permissions from the database.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? Email { get; }
    string? Name { get; }

    /// <summary>The account's security stamp when the sign-in was issued; it changes when the account is reset or taken over.</summary>
    string? SecurityStamp { get; }
    bool IsAuthenticated { get; }
}
