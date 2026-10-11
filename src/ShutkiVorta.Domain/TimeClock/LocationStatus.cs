namespace ShutkiVorta.Domain.TimeClock;

/// <summary>
/// What happened when the device was asked for its location at a clock-in or clock-out. Stored as an integer.
/// "Approximate" is not a status: it is a <see cref="Captured"/> location with an accuracy worse than 1000 m.
/// </summary>
public enum LocationStatus
{
    Captured = 1,
    PermissionDenied = 2,
    Unavailable = 3,
    Timeout = 4,
    NotSupported = 5,

    /// <summary>The form was posted without a location (e.g. scripts off).</summary>
    NotProvided = 6,

    /// <summary>Browsers only share locations over https.</summary>
    InsecureConnection = 7,

    /// <summary>A shift added or corrected by hand, not punched on a device.</summary>
    ManualEntry = 8,
}
