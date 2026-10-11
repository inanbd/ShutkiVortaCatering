using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.Domain.TimeClock;

/// <summary>
/// Where the device said it was at a clock-in or clock-out, or why it said nothing. The location is reported by the phone or
/// computer, so it is evidence, not proof.
/// </summary>
public sealed record PunchLocation
{
    /// <summary>Accuracies above this are not believable (browsers report a few metres up to a few kilometres).</summary>
    public const double MaxAccuracyMeters = 100_000;

    /// <summary>A location less precise than this is "approximate", e.g. an iPhone with Precise Location turned off.</summary>
    public const double ApproximateAccuracyMeters = 1000;

    private PunchLocation(LocationStatus status, double? latitude, double? longitude, double? accuracyMeters)
    {
        Status = status;
        Latitude = latitude;
        Longitude = longitude;
        AccuracyMeters = accuracyMeters;
    }

    public LocationStatus Status { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }

    /// <summary>The radius (metres) the device is fairly sure it is within.</summary>
    public double? AccuracyMeters { get; }

    public bool HasCoordinates => Status == LocationStatus.Captured;

    public bool IsApproximate => IsApproximateFix(Status, AccuracyMeters);

    /// <summary>For a shift added by hand: nobody's device was asked.</summary>
    public static PunchLocation ManualEntry { get; } = new(LocationStatus.ManualEntry, null, null, null);

    /// <summary>A location the device shared. Rejects coordinates no device could report (including NaN and infinity).</summary>
    public static PunchLocation Captured(double latitude, double longitude, double? accuracyMeters)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90 || !double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new DomainException("The location sent by your device could not be read. Please try again, or tap Skip.");
        }

        if (accuracyMeters is { } accuracy && (!double.IsFinite(accuracy) || accuracy is < 0 or > MaxAccuracyMeters))
        {
            throw new DomainException("The location sent by your device could not be read. Please try again, or tap Skip.");
        }

        return new PunchLocation(LocationStatus.Captured, latitude, longitude, accuracyMeters);
    }

    /// <summary>No location: <paramref name="status"/> says why (denied, timed out, no script…). Any coordinates are dropped.</summary>
    public static PunchLocation Missing(LocationStatus status)
    {
        if (status is LocationStatus.Captured or LocationStatus.ManualEntry || !Enum.IsDefined(status))
        {
            throw new DomainException("Unknown location status.");
        }

        return new PunchLocation(status, null, null, null);
    }

    public static bool IsApproximateFix(LocationStatus? status, double? accuracyMeters) =>
        status == LocationStatus.Captured && accuracyMeters > ApproximateAccuracyMeters;
}
