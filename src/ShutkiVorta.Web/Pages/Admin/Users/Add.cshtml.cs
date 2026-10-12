using System.ComponentModel.DataAnnotations;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Users;

/// <summary>
/// Adds someone to the staff. When the email already has an account with a confirmed email, the admin first sees whose it
/// is and confirms; then the person's page shows the set-password link once (and whether the email went out).
/// </summary>
[AdminOnly]
public sealed class AddModel(ISender sender, IDateTimeProvider clock) : AppPageModel(sender)
{
    [BindProperty]
    public AddInput Input { get; set; } = new();

    /// <summary>The existing account the admin confirmed adding (second step).</summary>
    [BindProperty]
    public string? ConfirmedUserId { get; set; }

    /// <summary>The email belongs to this existing account: shown for the admin to confirm.</summary>
    public ExistingAccountDto? ExistingAccount { get; private set; }

    /// <summary>The email belongs to someone on the staff already.</summary>
    public ExistingAccountDto? AlreadyStaff { get; private set; }

    public void OnGet() => ViewData["Title"] = "Add a user";

    public DateTime Local(DateTime utc) => clock.ToBusinessTime(utc);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Add a user";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        AddStaffMemberResult? result = null;
        var command = new AddStaffMemberCommand(Input.FullName, Input.Email, Input.PhoneNumber, Input.Role, Input.JobTitle, ConfirmedUserId);
        if (!await TryExecuteAsync(async () => result = await Sender.Send(command, cancellationToken), typeof(AddInput)))
        {
            return Page();
        }

        switch (result!.Outcome)
        {
            case AddStaffMemberOutcome.AlreadyStaff:
                AlreadyStaff = result.ExistingAccount;
                return Page();
            case AddStaffMemberOutcome.ConfirmExistingAccount:
                ExistingAccount = result.ExistingAccount;
                return Page();
        }

        var invitation = result.Invitation!;
        InvitationFlash.Save(TempData, invitation);
        StatusMessage = invitation.Kind == StaffInvitationKind.Added
            ? $"{invitation.FullName} is now on the staff as {invitation.Role.DisplayName()}. They sign in with their existing password."
            : $"{invitation.FullName} was added as {invitation.Role.DisplayName()}. They can sign in once they have set their password.";
        return Redirect(StaffUi.UserPath(invitation.UserId));
    }

    public sealed class AddInput
    {
        [Required(ErrorMessage = "Please enter their name."), StringLength(120)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter their email address."), EmailAddress, StringLength(256)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Phone, StringLength(30)]
        [Display(Name = "Mobile phone (optional)")]
        public string? PhoneNumber { get; set; }

        public StaffRole Role { get; set; } = StaffRole.Employee;

        [StringLength(100)]
        [Display(Name = "Job title (optional)")]
        public string? JobTitle { get; set; }
    }
}
