using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Users;

/// <summary>
/// What Managers and Employees may do: one level per module and role, saved per role (a save over someone else's change is
/// refused), with "Restore defaults". Admins always have everything; the admin-only parts are shown locked.
/// </summary>
[AdminOnly]
public sealed class PermissionsModel(ISender sender, IDateTimeProvider clock) : AppPageModel(sender)
{
    public PermissionsOverviewDto Overview { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Permissions";
        Overview = await Sender.Send(new GetPermissionsOverviewQuery(), cancellationToken);
    }

    /// <summary>Saves one role's levels (module key → level key) at the revision the page showed.</summary>
    public async Task<IActionResult> OnPostSaveAsync(StaffRole role, Dictionary<string, string>? levels, int revision, CancellationToken cancellationToken)
    {
        if (await TryExecuteAsync(() => Sender.Send(new SaveRolePermissionsCommand(role, levels ?? [], revision), cancellationToken)))
        {
            StatusMessage = $"{role.DisplayName()} permissions saved. They apply to every {role.DisplayName()} from their next click, except people with custom permissions.";
        }
        else
        {
            ErrorMessage = Errors();
        }

        return Redirect(StaffUi.PermissionsPath);
    }

    public async Task<IActionResult> OnPostRestoreAsync(StaffRole role, int revision, CancellationToken cancellationToken)
    {
        if (await TryExecuteAsync(() => Sender.Send(new RestoreRoleDefaultsCommand(role, revision), cancellationToken)))
        {
            StatusMessage = $"{role.DisplayName()} permissions are back to the defaults.";
        }
        else
        {
            ErrorMessage = Errors();
        }

        return Redirect(StaffUi.PermissionsPath);
    }

    public DateTime Local(DateTime utc) => clock.ToBusinessTime(utc);

    private string Errors() => string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
}
