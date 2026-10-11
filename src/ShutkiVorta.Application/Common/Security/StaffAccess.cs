namespace ShutkiVorta.Application.Common.Security;

/// <summary>
/// What one person may do in the admin area, worked out from the database on every request (never from cookie claims).
/// Staff access needs a staff role AND a confirmed email: invited people get in once they have set their password.
/// </summary>
public sealed class StaffAccess
{
    private static readonly IReadOnlySet<string> NoPermissions = new HashSet<string>();

    private StaffAccess(string? userId, StaffRole? role, bool emailConfirmed, bool usesCustomPermissions, IReadOnlySet<string> permissions)
    {
        UserId = userId;
        Role = role;
        EmailConfirmed = emailConfirmed;
        UsesCustomPermissions = usesCustomPermissions;
        Permissions = permissions;
    }

    /// <summary>Not signed in, or a background job: no staff access at all.</summary>
    public static StaffAccess None { get; } = new(null, null, false, false, NoPermissions);

    public string? UserId { get; }

    /// <summary>The person's staff role, even while their email is unconfirmed (they are not staff yet then).</summary>
    public StaffRole? Role { get; }

    public bool EmailConfirmed { get; }

    public bool UsesCustomPermissions { get; }

    /// <summary>The effective permissions (every permission for Admins; none unless <see cref="IsStaff"/>).</summary>
    public IReadOnlySet<string> Permissions { get; }

    public bool IsStaff => Role is not null && EmailConfirmed;

    public bool IsAdmin => IsStaff && Role == StaffRole.Admin;

    public bool Can(string permission) => IsAdmin || (IsStaff && Permissions.Contains(permission));

    /// <summary>
    /// Pure evaluation, so it can be unit tested.
    /// No staff role or an unconfirmed email ⇒ not staff. Several staff roles ⇒ the lowest counts (fail closed).
    /// Admin ⇒ everything. Custom permissions ⇒ exactly the person's own grants. Otherwise the role's defaults with the
    /// admin's changes for that role applied on top.
    /// </summary>
    /// <param name="roleOverrides">Per role, the permissions an admin turned on (true) or off (false) on the Permissions page.</param>
    public static StaffAccess Evaluate(
        string userId,
        IReadOnlyCollection<StaffRole> roles,
        bool emailConfirmed,
        bool usesCustom,
        IEnumerable<string> userGrants,
        IReadOnlyDictionary<StaffRole, IReadOnlyDictionary<string, bool>> roleOverrides)
    {
        var role = StaffRoles.Lowest(roles);
        if (role is not { } staffRole || !emailConfirmed)
        {
            return new StaffAccess(userId, role, emailConfirmed, usesCustom, NoPermissions);
        }

        if (staffRole == StaffRole.Admin)
        {
            return new StaffAccess(userId, staffRole, emailConfirmed, usesCustomPermissions: false, PermissionCatalog.Defaults(StaffRole.Admin));
        }

        if (usesCustom)
        {
            return new StaffAccess(userId, staffRole, emailConfirmed, usesCustom, PermissionCatalog.Normalize(userGrants));
        }

        var permissions = PermissionCatalog.ForRole(staffRole, roleOverrides.GetValueOrDefault(staffRole));
        return new StaffAccess(userId, staffRole, emailConfirmed, usesCustom, permissions);
    }
}
