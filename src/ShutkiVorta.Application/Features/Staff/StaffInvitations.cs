using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Emails;

namespace ShutkiVorta.Application.Features.Staff;

/// <summary>
/// Invitations: a new staff member gets a link to set their password (which also confirms their email, and only then
/// opens the admin area). The link is emailed, and shown once to the admin who added them so it can be texted too.
/// </summary>
public static class StaffInvitations
{
    /// <summary>How long an invitation link works (its own token provider, so password-reset links keep their short life).</summary>
    public static readonly TimeSpan ValidFor = TimeSpan.FromDays(7);

    public const string ExpiredOrUsed = "This invitation link has expired or was already used. Ask an admin to send a new one.";

    public const string InvitationSubject = "You're invited to join the team: set your password";
    public const string AddedSubject = "You've been added to the staff";
}

/// <summary>What happened when someone was added to the staff (or invited again).</summary>
public enum StaffInvitationKind
{
    /// <summary>They must set a password through the invitation link before they can sign in.</summary>
    Invited = 1,

    /// <summary>An existing account with a confirmed email: they keep their password and were told by email.</summary>
    Added = 2,
}

/// <summary>
/// The result of adding someone or sending their invitation again. <see cref="Link"/> is the secret set-password link: it is
/// shown to the admin once and never stored or logged.
/// </summary>
public sealed class StaffInvitationDto
{
    public string UserId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public StaffRole Role { get; init; }
    public StaffInvitationKind Kind { get; init; }

    /// <summary>The set-password link (null when an existing account was added: they already have a password).</summary>
    public string? Link { get; init; }

    /// <summary>False when the email was not sent because the website address (Site:BaseUrl) is not set.</summary>
    public bool Emailed { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    /// <summary>When the link stops working.</summary>
    public DateTime ExpiresAtUtc => CreatedAtUtc + StaffInvitations.ValidFor;
}

/// <summary>Reads what happened to the emails sent to staff (from the email log), so the Users pages can say "Sent" or "Saved to folder".</summary>
public interface IStaffEmailLog
{
    /// <summary>The newest email to <paramref name="toAddress"/> with one of <paramref name="subjects"/>, or null.</summary>
    Task<StaffEmailStatusDto?> GetLatestAsync(string toAddress, IReadOnlyCollection<string> subjects, CancellationToken cancellationToken = default);
}

/// <summary>One email from the log, without its content (invitations contain a private link).</summary>
public sealed class StaffEmailStatusDto
{
    public long Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public EmailStatus Status { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? SentAtUtc { get; init; }

    /// <summary>Still in the queue: the page can look again in a moment.</summary>
    public bool IsWaiting => Status is EmailStatus.Pending or EmailStatus.Sending;
}
