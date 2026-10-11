namespace ShutkiVorta.Domain.TimeClock;

/// <summary>How a shift was closed. Stored as an integer.</summary>
public enum ClockOutSource
{
    /// <summary>The person clocked out themselves.</summary>
    Self = 1,

    /// <summary>The person forgot and entered when they left afterwards (flagged for review).</summary>
    SelfCorrected = 2,

    /// <summary>Closed or corrected by someone with timeclock.manage.</summary>
    Manager = 3,

    /// <summary>Closed by the system, e.g. when the person was removed from the staff.</summary>
    System = 4,

    /// <summary>The person forgot and doesn't remember when they left; excluded from hours until fixed.</summary>
    Missed = 5,
}
