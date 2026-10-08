namespace ShutkiVorta.Application.Common.Security;

/// <summary>Marker for requests that only administrators may send. Enforced by the authorization pipeline behavior.</summary>
public interface IRequireAdmin;

/// <summary>Marker for requests that require a signed-in user.</summary>
public interface IRequireAuthenticatedUser;
