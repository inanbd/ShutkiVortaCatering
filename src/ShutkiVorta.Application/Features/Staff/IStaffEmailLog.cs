using ShutkiVorta.Application.Features.Emails;

namespace ShutkiVorta.Application.Features.Staff;

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
