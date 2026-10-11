using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Users;

/// <summary>Who works here: the staff list with roles and what stands out (read-only except for Admins), and former staff.</summary>
[RequirePermission(Permissions.StaffView)]
public sealed class IndexModel(ISender sender, ICurrentStaff currentStaff, IDateTimeProvider clock) : AppPageModel(sender)
{
    public const string FormerTab = "former";

    /// <summary>admin, manager or employee (empty: every role).</summary>
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    /// <summary>"former" for former staff.</summary>
    [BindProperty(SupportsGet = true)] public string? Show { get; set; }

    public StaffListDto List { get; private set; } = new();
    public bool IsAdmin { get; private set; }
    public DateTime NowLocal { get; private set; }

    public bool ShowFormer => string.Equals(Show, FormerTab, StringComparison.OrdinalIgnoreCase);

    public StaffRole? RoleFilter =>
        Enum.TryParse<StaffRole>(Role, ignoreCase: true, out var role) && StaffRoles.All.Contains(role) ? role : null;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Users";
        IsAdmin = (await currentStaff.GetAsync(cancellationToken)).IsAdmin;
        NowLocal = clock.BusinessNow;
        List = await Sender.Send(new GetStaffListQuery(ShowFormer ? null : RoleFilter, Search, ShowFormer), cancellationToken);
    }

    public DateTime? Local(DateTime? utc) => utc is { } value ? clock.ToBusinessTime(value) : null;

    /// <summary>This list with the current search on another tab.</summary>
    public string TabUrl(bool former) => QueryHelpers.AddQueryString(StaffUi.UsersPath, new Dictionary<string, string?>
    {
        ["show"] = former ? FormerTab : null,
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
    }.Where(kv => kv.Value is not null));
}
