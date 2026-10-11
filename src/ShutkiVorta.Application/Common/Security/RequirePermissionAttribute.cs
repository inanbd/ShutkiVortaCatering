namespace ShutkiVorta.Application.Common.Security;

/// <summary>
/// Only staff with <see cref="Permission"/> (and every Admin) may send this request or use this admin page or handler.
/// On a request record it is enforced by the authorization pipeline behavior; on a page model class or handler method by
/// the Web project's permission page filter (class and handler attributes must both be satisfied).
/// A plain attribute on purpose: Razor Pages ignores filter attributes on handler methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute(string permission) : Attribute
{
    /// <summary>A key from <see cref="Permissions"/>.</summary>
    public string Permission { get; } = PermissionCatalog.IsKnown(permission)
        ? permission
        : throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
}
