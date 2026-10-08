using ShutkiVorta.Application.Common.Models;

namespace ShutkiVorta.Application.Features.Accounts;

/// <summary>Abstraction over ASP.NET Core Identity so application logic stays framework-agnostic.</summary>
public interface IIdentityService
{
    bool RequireConfirmedEmail { get; }

    Task<Result<string>> RegisterCustomerAsync(string fullName, string email, string phone, string password);

    Task<SignInOutcome> PasswordSignInAsync(string email, string password, bool rememberMe);

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

    Task<PagedResult<UserAccount>> SearchUsersAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Result> SetAdminRoleAsync(string userId, bool isAdmin);
}

public enum SignInOutcome
{
    Succeeded,
    Failed,
    LockedOut,
    EmailNotConfirmed,
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
    public bool IsAdmin { get; init; }
    public bool IsLockedOut { get; init; }
}

public sealed record UserProfileUpdate(
    string FullName,
    string? PhoneNumber,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode);
