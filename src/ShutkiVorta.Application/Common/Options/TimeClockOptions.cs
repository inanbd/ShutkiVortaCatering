namespace ShutkiVorta.Application.Common.Options;

/// <summary>
/// Time clock settings (Admin → Settings → Time clock). The kitchen location is private: it is only used to show how far from
/// the kitchen staff clocked in, and is separate from the public map position in Business details.
/// </summary>
public sealed class TimeClockOptions
{
    public const string SectionName = "TimeClock";

    /// <summary>Empty (with the longitude) when the kitchen location is not set: distances are then not recorded.</summary>
    public double? KitchenLatitude { get; set; }

    public double? KitchenLongitude { get; set; }

    /// <summary>Punches farther from the kitchen than this are flagged for review.</summary>
    public int NearKitchenRadiusMeters { get; set; } = 300;

    /// <summary>Weekly hours (and overtime over 40 hours) are counted from this day.</summary>
    public DayOfWeek WorkweekStartsOn { get; set; } = DayOfWeek.Monday;

    /// <summary>Refuse to clock in or out without the device's location (a manager can still add the shift by hand).</summary>
    public bool RequireLocation { get; set; }

    public bool HasKitchenLocation => KitchenLatitude is not null && KitchenLongitude is not null;
}
