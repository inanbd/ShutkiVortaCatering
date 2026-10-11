namespace ShutkiVorta.Application.Common.Security;

/// <summary>
/// Marker for requests that only Admins may send (checked against the database, never the cookie).
/// Enforced by the authorization pipeline behavior.
/// </summary>
public interface IRequireAdmin;

/// <summary>Marker for requests any staff member may send (Admin, Manager or Employee with a confirmed email).</summary>
public interface IRequireStaff;

/// <summary>Marker for requests that require a signed-in user.</summary>
public interface IRequireAuthenticatedUser;
