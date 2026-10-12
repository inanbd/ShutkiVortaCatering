using ShutkiVorta.Application.Common.Models;

namespace ShutkiVorta.Application.Features.Accounts;

/// <summary>Abstraction over ASP.NET Core Identity so application logic stays framework-agnostic.</summary>
public interface IIdentityService
{
    bool RequireConfirmedEmail { get; }

    Task<Result<string>> RegisterCustomerAsync(string fullName, string email, string phone, string password);

    /// <summary>
    /// Signs in with a password. On success the result carries the user's id: the cookie is written, but the current
    /// request still sees an anonymous user.
    /// </summary>
    Task<SignInResultDto> PasswordSignInAsync(string email, string password, bool rememberMe);

    Task SignInAsync(string userId, bool isPersistent);

    Task RefreshSignInAsync(string userId);

    Task SignOutAsync();

    Task<UserAccount?> FindByIdAsync(string userId);

    Task<UserAccount?> FindByEmailAsync(string email);

    /// <summary>Returns a URL-safe email confirmation code.</summary>
    Task<string> GenerateEmailConfirmationCodeAsync(string userId);

    Task<Result> ConfirmEmailAsync(string userId, string code);

    /// <summary>Returns a URL-safe password reset code, or <c>null</c> when no such user exists.</summary>
    Task<string?> GeneratePasswordResetCodeAsync(string email);

    Task<Result> ResetPasswordAsync(string email, string code, string newPassword);

    Task<Result> ChangePasswordAsync(string userId, string currentPassword, string newPassword);

    Task<Result> UpdateProfileAsync(string userId, UserProfileUpdate update);

    // ---- Staff accounts (the Users pages, for Admins) ----

    /// <summary>
    /// Creates the account of a new staff member: no password, email not confirmed and no Customer role. The person gets in
    /// by setting a password through their invitation link, which confirms the email. Returns the new user's id.
    /// </summary>
    Task<Result<string>> CreateStaffAccountAsync(string fullName, string email, string? phoneNumber);

    /// <summary>
    /// Removes the password (if any) and rotates the security stamp, so the account can only be entered through a new
    /// invitation: its sessions end and every earlier invitation or password-reset link stops working. Used to take over an
    /// account whose email was never confirmed before it becomes staff, and before an invitation is sent again.
    /// </summary>
    Task<Result> ResetToInvitationAsync(string userId);

    /// <summary>Rotates the security stamp: the person is signed out everywhere and earlier links sent to them stop working.</summary>
    Task<Result> RotateSecurityStampAsync(string userId);

    /// <summary>
    /// Changes the email (which is also the user name) of an account that has no password yet, e.g. a mistyped invitation
    /// address. Earlier invitation links stop working. Fails for an account with a password or an email already taken.
    /// </summary>
    Task<Result> ChangeEmailWithoutPasswordAsync(string userId, string newEmail);

    /// <summary>Changes the name and phone number, keeping the address.</summary>
    Task<Result> UpdateContactAsync(string userId, string fullName, string? phoneNumber);

    /// <summary>Lets the person sign in again after too many wrong passwords (clears the lockout and the failed attempts).</summary>
    Task<Result> UnlockAsync(string userId);

    /// <summary>
    /// A URL-safe staff invitation code for the account. It works once, for <c>StaffInvitations.ValidFor</c>, and only until
    /// the account's security stamp changes (a new invitation, a changed email, a password).
    /// </summary>
    Task<string> GenerateStaffInvitationCodeAsync(string userId);

    /// <summary>Whether a staff invitation code can still be used (it is not consumed).</summary>
    Task<bool> IsStaffInvitationValidAsync(string email, string code);

    /// <summary>
    /// Sets the first password of an invited staff member with their invitation code and confirms their email (following
    /// the link proves they own the address). Returns the user's id. An expired, used or replaced code fails with
    /// <c>StaffInvitations.ExpiredOrUsed</c>; a weak password with the password rules.
    /// </summary>
    Task<Result<string>> AcceptStaffInvitationAsync(string email, string code, string password);
}

public enum SignInOutcome
{
    Succeeded,
    Failed,
    LockedOut,
    EmailNotConfirmed,
}

/// <param name="UserId">The signed-in user's id when <paramref name="Outcome"/> is Succeeded, otherwise null.</param>
public sealed record SignInResultDto(SignInOutcome Outcome, string? UserId)
{
    public static SignInResultDto Failed(SignInOutcome outcome) => new(outcome, null);
}

public sealed record UserAccount
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public bool EmailConfirmed { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public bool IsLockedOut { get; init; }

    /// <summary>False for invited staff who have not set their password yet.</summary>
    public bool HasPassword { get; init; }
}

public sealed record UserProfileUpdate(
    string FullName,
    string? PhoneNumber,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode);
