using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Users;

/// <summary>
/// One staff member: details, what they may do, time clock and recent changes. Anyone with staff.view can look; every change
/// is for Admins. Each form posts the version it was shown, so a change made meanwhile is noticed instead of overwritten.
/// </summary>
[RequirePermission(Permissions.StaffView)]
public sealed class UserModel(ISender sender, ICurrentStaff currentStaff, IDateTimeProvider clock) : AppPageModel(sender)
{
    public const string FollowRole = "role";
    public const string Custom = "custom";

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public StaffMemberDetailsDto Details { get; private set; } = new();
    public StaffMemberDto Member => Details.Member;
    public StaffAccess Viewer { get; private set; } = StaffAccess.None;
    public bool IsAdmin => Viewer.IsAdmin;
    public bool IsYou => string.Equals(Viewer.UserId, Member.UserId, StringComparison.OrdinalIgnoreCase);
    public DateTime NowLocal { get; private set; }

    /// <summary>The set-password link of the invitation just sent: shown this once.</summary>
    public InvitationFlash? Invitation { get; private set; }

    /// <summary>The details form as posted, when it needs fixing.</summary>
    public DetailsInput DetailsForm { get; private set; } = new();

    /// <summary>This workweek's finished hours and the open shift so far (only for people who may see the team's hours).</summary>
    public PersonWeekHoursDto? ThisWeek { get; private set; }
    public bool CanSeeHours { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Invitation = IsAdmin ? InvitationFlash.Take(TempData, Member.UserId) : null;
        return Page();
    }

    /// <summary>The delivery status of the newest invitation email, for the page to update while it is still queued.</summary>
    [AdminOnly]
    public async Task<IActionResult> OnGetEmailStatusAsync(CancellationToken cancellationToken)
    {
        var email = await Sender.Send(new GetStaffEmailStatusQuery(Id), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(email is null
            ? new { text = "No email yet", css = "pill-off", waiting = false }
            : new { text = StaffUi.EmailStatusText(email), css = StaffUi.EmailStatusCss(email.Status), waiting = email.IsWaiting });
    }

    [AdminOnly]
    public async Task<IActionResult> OnPostDetailsAsync(DetailsInput details, int version, CancellationToken cancellationToken)
    {
        var command = new UpdateStaffDetailsCommand(Id, details.FullName ?? string.Empty, details.PhoneNumber, details.JobTitle, version);
        if (await TryExecuteAsync(() => Sender.Send(command, cancellationToken), typeof(UpdateStaffDetailsCommand), prefix: "details"))
        {
            StatusMessage = "Details saved.";
            return Redirect(StaffUi.UserPath(Id));
        }

        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        DetailsForm = details;
        return Page();
    }

    [AdminOnly]
    public Task<IActionResult> OnPostEmailAsync(string? email, CancellationToken cancellationToken) =>
        InviteAsync(new ChangeStaffEmailCommand(Id, email ?? string.Empty), cancellationToken);

    [AdminOnly]
    public Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken) =>
        InviteAsync(new ResendStaffInvitationCommand(Id), cancellationToken);

    [AdminOnly]
    public Task<IActionResult> OnPostReAddAsync(StaffRole role, CancellationToken cancellationToken) =>
        InviteAsync(new ReAddStaffMemberCommand(Id, role), cancellationToken);

    [AdminOnly]
    public Task<IActionResult> OnPostRoleAsync(StaffRole role, int version, CancellationToken cancellationToken) =>
        ChangeAsync(new ChangeStaffRoleCommand(Id, role, version), $"Role changed to {role.DisplayName()}.", cancellationToken);

    /// <summary>"role" to follow the role's permissions, "custom" for the levels chosen (module key → level key).</summary>
    [AdminOnly]
    public Task<IActionResult> OnPostPermissionsAsync(string? mode, Dictionary<string, string>? levels, int version, CancellationToken cancellationToken)
    {
        var custom = string.Equals(mode, Custom, StringComparison.OrdinalIgnoreCase);
        var command = new SetStaffPermissionsCommand(Id, custom ? levels ?? [] : null, version);
        return ChangeAsync(command, custom ? "Custom permissions saved. They apply from their next click." : "They follow their role's permissions again.", cancellationToken);
    }

    [AdminOnly]
    public Task<IActionResult> OnPostUnlockAsync(CancellationToken cancellationToken) =>
        ChangeAsync(new UnlockStaffSignInCommand(Id), "Unlocked: they can sign in again.", cancellationToken);

    [AdminOnly]
    public Task<IActionResult> OnPostRemoveAsync(int version, CancellationToken cancellationToken) =>
        ChangeAsync(new RemoveFromStaffCommand(Id, version), "Removed from the staff. Their account and shifts are kept.", cancellationToken);

    public DateTime? Local(DateTime? utc) => utc is { } value ? clock.ToBusinessTime(value) : null;

    private async Task<IActionResult> ChangeAsync(IRequest command, string done, CancellationToken cancellationToken)
    {
        if (await TryExecuteAsync(() => Sender.Send(command, cancellationToken)))
        {
            StatusMessage = done;
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect(StaffUi.UserPath(Id));
    }

    /// <summary>A change that may turn out to be no change at all (false: nothing was saved).</summary>
    private async Task<IActionResult> ChangeAsync(IRequest<bool> command, string done, CancellationToken cancellationToken)
    {
        var changed = false;
        if (await TryExecuteAsync(async () => changed = await Sender.Send(command, cancellationToken)))
        {
            StatusMessage = changed ? done : StaffUi.NothingChanged;
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect(StaffUi.UserPath(Id));
    }

    /// <summary>Sends an invitation (or "you've been added") and shows its link once on this page.</summary>
    private async Task<IActionResult> InviteAsync(IRequest<StaffInvitationDto> command, CancellationToken cancellationToken)
    {
        StaffInvitationDto? invitation = null;
        if (await TryExecuteAsync(async () => invitation = await Sender.Send(command, cancellationToken)))
        {
            InvitationFlash.Save(TempData, invitation!);
            StatusMessage = invitation!.Kind == StaffInvitationKind.Added
                ? $"{invitation.FullName} is on the staff again as {invitation.Role.DisplayName()}. They sign in with their existing password."
                : $"A new invitation is ready for {invitation.Email}. Earlier links no longer work.";
        }
        else
        {
            ErrorMessage = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
        }

        return Redirect(StaffUi.UserPath(Id));
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        Viewer = await currentStaff.GetAsync(cancellationToken);
        if (await Sender.Send(new GetStaffMemberQuery(Id), cancellationToken) is not { } details)
        {
            return false;
        }

        Details = details;
        DetailsForm = new DetailsInput { FullName = Member.FullName, PhoneNumber = Member.PhoneNumber, JobTitle = Member.JobTitle };
        ViewData["Title"] = Member.FullName;
        NowLocal = clock.BusinessNow;

        // The time clock's team hours are a permission of their own: without it the page just links to nothing.
        CanSeeHours = Viewer.Can(Permissions.TimeClockViewAll);
        if (CanSeeHours && Member.Role is not null)
        {
            var today = DateOnly.FromDateTime(NowLocal);
            var weekly = await Sender.Send(new GetWeeklyHoursQuery { UserId = Member.UserId, From = today, To = today }, cancellationToken);
            ThisWeek = weekly.Weeks.FirstOrDefault()?.People.FirstOrDefault(p => string.Equals(p.UserId, Member.UserId, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }

    public sealed class DetailsInput
    {
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? JobTitle { get; set; }
    }
}
