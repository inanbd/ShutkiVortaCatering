using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Application.Features.Staff;

namespace ShutkiVorta.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOptionsMonitor<AccountOptions> account) : IIdentityService
{
    public bool RequireConfirmedEmail => account.CurrentValue.RequireConfirmedEmail;

    public async Task<Result<string>> RegisterCustomerAsync(string fullName, string email, string phone, string password)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            PhoneNumber = phone,
            CreatedAtUtc = DateTime.UtcNow,
            LockoutEnabled = true,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return Result<string>.Failure(result.Errors.Select(e => e.Description));
        }

        await userManager.AddToRoleAsync(user, Roles.Customer);
        return Result<string>.Success(user.Id);
    }

    public async Task<SignInResultDto> PasswordSignInAsync(string email, string password, bool rememberMe)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return SignInResultDto.Failed(SignInOutcome.Failed);
        }

        var result = await signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return new SignInResultDto(SignInOutcome.Succeeded, user.Id);
        }

        if (result.IsLockedOut)
        {
            return SignInResultDto.Failed(SignInOutcome.LockedOut);
        }

        // IsNotAllowed is only returned after the password was verified, so this does not leak account existence.
        return SignInResultDto.Failed(result.IsNotAllowed && !user.EmailConfirmed ? SignInOutcome.EmailNotConfirmed : SignInOutcome.Failed);
    }

    public async Task SignInAsync(string userId, bool isPersistent)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null)
        {
            await signInManager.SignInAsync(user, isPersistent);
        }
    }

    public async Task RefreshSignInAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();

    public async Task<UserAccount?> FindByIdAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<UserAccount?> FindByEmailAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<string> GenerateEmailConfirmationCodeAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("User not found.");
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return Encode(token);
    }

    public async Task<Result> ConfirmEmailAsync(string userId, string code)
    {
        var user = await userManager.FindByIdAsync(userId);
        var token = Decode(code);
        if (user is null || token is null)
        {
            return Result.Failure("This confirmation link is invalid or has expired.");
        }

        if (user.EmailConfirmed)
        {
            return Result.Success();
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded ? Result.Success() : Result.Failure("This confirmation link is invalid or has expired.");
    }

    public async Task<string?> GeneratePasswordResetCodeAsync(string email)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        return Encode(await userManager.GeneratePasswordResetTokenAsync(user));
    }

    public async Task<Result> ResetPasswordAsync(string email, string code, string newPassword)
    {
        var user = await userManager.FindByEmailAsync(email);
        var token = Decode(code);
        if (user is null || token is null)
        {
            return Result.Failure("This password reset link is invalid or has expired.");
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded && !user.EmailConfirmed)
        {
            // Following an emailed reset link proves ownership of the address.
            var refreshed = await userManager.FindByIdAsync(user.Id);
            if (refreshed is not null)
            {
                refreshed.EmailConfirmed = true;
                await userManager.UpdateAsync(refreshed);
            }
        }

        return result.Succeeded ? Result.Success() : Result.Failure(result.Errors.Select(e => e.Description));
    }

    public async Task<Result> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            return Result.Failure(result.Errors.Select(e => e.Description));
        }

        await signInManager.RefreshSignInAsync(user);
        return Result.Success();
    }

    public async Task<Result> UpdateProfileAsync(string userId, UserProfileUpdate update)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        user.FullName = update.FullName;
        user.PhoneNumber = update.PhoneNumber;
        user.AddressLine1 = update.AddressLine1;
        user.AddressLine2 = update.AddressLine2;
        user.City = update.City;
        user.State = update.State;
        user.PostalCode = update.PostalCode;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded ? Result.Success() : Result.Failure(result.Errors.Select(e => e.Description));
    }

    // ---- Staff accounts ----

    public async Task<Result<string>> CreateStaffAccountAsync(string fullName, string email, string? phoneNumber)
    {
        // No password and an unconfirmed email: the invitation link sets the one and confirms the other. No Customer role.
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            CreatedAtUtc = DateTime.UtcNow,
            LockoutEnabled = true,
        };

        var result = await userManager.CreateAsync(user);
        return result.Succeeded ? Result<string>.Success(user.Id) : Result<string>.Failure(result.Errors.Select(e => e.Description));
    }

    public async Task<Result> ResetToInvitationAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        // Clears the hash and rotates the security stamp in one update.
        return ToResult(await userManager.RemovePasswordAsync(user));
    }

    public async Task<Result> RotateSecurityStampAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null ? Result.Failure("User not found.") : ToResult(await userManager.UpdateSecurityStampAsync(user));
    }

    public async Task<Result> ChangeEmailWithoutPasswordAsync(string userId, string newEmail)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        if (await userManager.HasPasswordAsync(user))
        {
            return Result.Failure("This person has already set a password, so their email can't be changed here.");
        }

        if (await userManager.FindByEmailAsync(newEmail) is { } other && other.Id != user.Id)
        {
            return Result.Failure($"{newEmail} already belongs to another account.");
        }

        // The email is also the user name. One update (which validates and normalises both) with a new security stamp, so
        // links sent to the old address stop working.
        user.Email = newEmail;
        user.UserName = newEmail;
        user.EmailConfirmed = false;
        return ToResult(await userManager.UpdateSecurityStampAsync(user));
    }

    public async Task<Result> UpdateContactAsync(string userId, string fullName, string? phoneNumber)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        user.FullName = fullName;
        user.PhoneNumber = phoneNumber;
        return ToResult(await userManager.UpdateAsync(user));
    }

    public async Task<Result> UnlockAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        if (await userManager.GetLockoutEnabledAsync(user))
        {
            var unlocked = await userManager.SetLockoutEndDateAsync(user, null);
            if (!unlocked.Succeeded)
            {
                return ToResult(unlocked);
            }
        }

        return ToResult(await userManager.ResetAccessFailedCountAsync(user));
    }

    public async Task<string> GenerateStaffInvitationCodeAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId) ?? throw new InvalidOperationException("User not found.");
        return Encode(await userManager.GenerateUserTokenAsync(user, StaffInvitationTokenProvider.ProviderName, StaffInvitationTokenProvider.Purpose));
    }

    public async Task<bool> IsStaffInvitationValidAsync(string email, string code) => await FindInvitedAsync(email, code) is not null;

    public async Task<Result<string>> AcceptStaffInvitationAsync(string email, string code, string password)
    {
        var user = await FindInvitedAsync(email, code);
        if (user is null)
        {
            return Result<string>.Failure(StaffInvitations.ExpiredOrUsed);
        }

        // Following the emailed link proves ownership of the address. Saved together with the password (one update, which
        // also rotates the security stamp, so the link cannot be used again); nothing is saved if the password is refused.
        user.EmailConfirmed = true;
        var result = await userManager.AddPasswordAsync(user, password);
        return result.Succeeded ? Result<string>.Success(user.Id) : Result<string>.Failure(result.Errors.Select(e => e.Description));
    }

    /// <summary>The account the invitation code was made for, while it can still be used (no password set yet), or null.</summary>
    private async Task<ApplicationUser?> FindInvitedAsync(string email, string code)
    {
        var token = Decode(code);
        var user = string.IsNullOrWhiteSpace(email) ? null : await userManager.FindByEmailAsync(email.Trim());
        if (user is null || token is null || await userManager.HasPasswordAsync(user))
        {
            return null;
        }

        return await userManager.VerifyUserTokenAsync(user, StaffInvitationTokenProvider.ProviderName, StaffInvitationTokenProvider.Purpose, token)
            ? user
            : null;
    }

    private static Result ToResult(IdentityResult result) =>
        result.Succeeded ? Result.Success() : Result.Failure(result.Errors.Select(e => e.Description));

    private async Task<UserAccount> ToAccountAsync(ApplicationUser user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FullName = user.FullName,
        PhoneNumber = user.PhoneNumber,
        EmailConfirmed = user.EmailConfirmed,
        AddressLine1 = user.AddressLine1,
        AddressLine2 = user.AddressLine2,
        City = user.City,
        State = user.State,
        PostalCode = user.PostalCode,
        CreatedAtUtc = user.CreatedAtUtc,
        IsLockedOut = await userManager.IsLockedOutAsync(user),
        HasPassword = user.PasswordHash is not null,
    };

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? Decode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
