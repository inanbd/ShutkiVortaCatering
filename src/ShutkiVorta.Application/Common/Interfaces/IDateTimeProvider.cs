namespace ShutkiVorta.Application.Common.Interfaces;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }

    /// <summary>Current wall-clock time at the business (Dallas, Central Time).</summary>
    DateTime BusinessNow { get; }

    TimeZoneInfo BusinessTimeZone { get; }

    DateTime ToBusinessTime(DateTime utc);

    DateTime ToUtc(DateTime businessLocal);
}
