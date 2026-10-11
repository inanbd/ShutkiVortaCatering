namespace ShutkiVorta.Application.Features.Staff;

/// <summary>Reads the staff list. Implemented in Infrastructure; feature code calls it but does not change it.</summary>
public interface IStaffDirectory
{
    /// <summary>Staff sorted by role (Admins first), then name.</summary>
    Task<IReadOnlyList<StaffMemberDto>> ListAsync(StaffListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>A staff member or a former one (Role null); null when the account was never staff or does not exist.</summary>
    Task<StaffMemberDto?> GetAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>People with a staff profile but no staff role any more, sorted by name.</summary>
    Task<IReadOnlyList<StaffMemberDto>> ListFormerAsync(CancellationToken cancellationToken = default);
}
