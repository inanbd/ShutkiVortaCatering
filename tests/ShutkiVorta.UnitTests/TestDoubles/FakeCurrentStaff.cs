using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;

namespace ShutkiVorta.UnitTests.TestDoubles;

/// <summary>The signed-in person's staff access, set by the test (no staff access by default).</summary>
internal sealed class FakeCurrentStaff : ICurrentStaff
{
    public FakeCurrentStaff(StaffAccess? access = null) => Access = access ?? StaffAccess.None;

    public StaffAccess Access { get; set; }

    public ValueTask<StaffAccess> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Access);

    public static FakeCurrentStaff None() => new();

    public static FakeCurrentStaff Admin(string userId = "admin-1") => new(StaffAccessFor.Admin(userId));

    /// <summary>A Manager or Employee with exactly these permissions (custom permissions, normalised).</summary>
    public static FakeCurrentStaff With(StaffRole role, params string[] permissions) => new(StaffAccessFor.Custom(role, permissions));

    /// <summary>A Manager or Employee with their role's default permissions.</summary>
    public static FakeCurrentStaff Defaults(StaffRole role, string userId = "staff-1") => new(StaffAccessFor.Defaults(role, userId));
}

/// <summary>Builds <see cref="StaffAccess"/> values through the real evaluation.</summary>
internal static class StaffAccessFor
{
    private static readonly IReadOnlyDictionary<StaffRole, IReadOnlyDictionary<string, bool>> NoOverrides =
        new Dictionary<StaffRole, IReadOnlyDictionary<string, bool>>();

    public static StaffAccess Admin(string userId = "admin-1") =>
        StaffAccess.Evaluate(userId, [StaffRole.Admin], emailConfirmed: true, usesCustom: false, [], NoOverrides);

    public static StaffAccess Defaults(StaffRole role, string userId = "staff-1") =>
        StaffAccess.Evaluate(userId, [role], emailConfirmed: true, usesCustom: false, [], NoOverrides);

    public static StaffAccess Custom(StaffRole role, IEnumerable<string> permissions, string userId = "staff-1") =>
        StaffAccess.Evaluate(userId, [role], emailConfirmed: true, usesCustom: true, permissions, NoOverrides);
}
