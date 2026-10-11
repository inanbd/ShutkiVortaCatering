namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>Who is on the clock right now. Times are UTC (Kind = Utc).</summary>
public interface ITimeClockStatusReader
{
    /// <summary>The start of each person's open shift; people who are not on the clock are left out.</summary>
    Task<IReadOnlyDictionary<string, DateTime>> GetOpenShiftStartsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

    /// <summary>When the person's open shift started, or null when they are not on the clock.</summary>
    Task<DateTime?> GetOpenShiftStartAsync(string userId, CancellationToken cancellationToken = default);
}
