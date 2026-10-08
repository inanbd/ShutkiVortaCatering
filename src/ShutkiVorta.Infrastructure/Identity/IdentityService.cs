using System.Text;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Accounts;
using ShutkiVorta.Infrastructure.Persistence;

namespace ShutkiVorta.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IDbConnectionFactory connections,
    ISqlDialect dialect) : IIdentityService
{
    public bool RequireConfirmedEmail => userManager.Options.SignIn.RequireConfirmedEmail;

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

    public async Task<SignInOutcome> PasswordSignInAsync(string email, string password, bool rememberMe)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return SignInOutcome.Failed;
        }

        var result = await signInManager.PasswordSignInAsync(user, password, rememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            return SignInOutcome.Succeeded;
        }

        if (result.IsLockedOut)
        {
            return SignInOutcome.LockedOut;
        }

        // IsNotAllowed is only returned after the password was verified, so this does not leak account existence.
        return result.IsNotAllowed && !user.EmailConfirmed ? SignInOutcome.EmailNotConfirmed : SignInOutcome.Failed;
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

    public async Task<PagedResult<UserAccount>> SearchUsersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var where = string.Empty;
        var parameters = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Replace("%", string.Empty).Replace("_", string.Empty).Replace("[", string.Empty).Trim();
            where = "WHERE u.Email LIKE @Pattern OR u.FullName LIKE @Pattern OR u.PhoneNumber LIKE @Pattern";
            parameters.Add("Pattern", $"%{term}%");
        }

        parameters.Add("Skip", (page - 1) * pageSize);
        parameters.Add("Take", pageSize);
        parameters.Add("AdminRole", Roles.Admin.ToUpperInvariant());

        await using var connection = await connections.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM Users u {where}", parameters, cancellationToken: cancellationToken));
        if (total == 0)
        {
            return PagedResult<UserAccount>.Empty(page, pageSize);
        }

        var sql = dialect.Page($"""
            SELECT u.Id, u.Email, u.FullName, u.PhoneNumber, u.EmailConfirmed, u.AddressLine1, u.AddressLine2, u.City, u.State,
                   u.PostalCode, u.CreatedAtUtc, u.LockoutEndUtc,
                   (SELECT COUNT(*) FROM UserRoles ur INNER JOIN Roles r ON r.Id = ur.RoleId
                    WHERE ur.UserId = u.Id AND r.NormalizedName = @AdminRole) AS AdminRoleCount
            FROM Users u {where}
            ORDER BY u.CreatedAtUtc DESC
            """);

        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var now = DateTime.UtcNow;
        var items = rows.Select(r => new UserAccount
        {
            Id = r.Id,
            Email = r.Email,
            FullName = r.FullName,
            PhoneNumber = r.PhoneNumber,
            EmailConfirmed = r.EmailConfirmed,
            AddressLine1 = r.AddressLine1,
            AddressLine2 = r.AddressLine2,
            City = r.City,
            State = r.State,
            PostalCode = r.PostalCode,
            CreatedAtUtc = r.CreatedAtUtc,
            IsAdmin = r.AdminRoleCount > 0,
            IsLockedOut = r.LockoutEndUtc is { } end && end > now,
        }).ToList();

        return new PagedResult<UserAccount>(items, total, page, pageSize);
    }

    public async Task<Result> SetAdminRoleAsync(string userId, bool isAdmin)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        var inRole = await userManager.IsInRoleAsync(user, Roles.Admin);
        IdentityResult result;
        if (isAdmin && !inRole)
        {
            result = await userManager.AddToRoleAsync(user, Roles.Admin);
        }
        else if (!isAdmin && inRole)
        {
            result = await userManager.RemoveFromRoleAsync(user, Roles.Admin);
        }
        else
        {
            return Result.Success();
        }

        if (result.Succeeded)
        {
            // Invalidate existing cookies so the role change applies on the user's next request.
            await userManager.UpdateSecurityStampAsync(user);
        }

        return result.Succeeded ? Result.Success() : Result.Failure(result.Errors.Select(e => e.Description));
    }

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
        IsAdmin = await userManager.IsInRoleAsync(user, Roles.Admin),
        IsLockedOut = await userManager.IsLockedOutAsync(user),
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

    private sealed class UserRow
    {
        public string Id { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string? PhoneNumber { get; init; }
        public bool EmailConfirmed { get; init; }
        public string? AddressLine1 { get; init; }
        public string? AddressLine2 { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? PostalCode { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime? LockoutEndUtc { get; init; }
        public int AdminRoleCount { get; init; }
    }
}
