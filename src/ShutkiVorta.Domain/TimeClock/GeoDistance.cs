namespace ShutkiVorta.Domain.TimeClock;

/// <summary>Distances on the Earth's surface.</summary>
public static class GeoDistance
{
    /// <summary>The Earth's mean radius in metres (IUGG).</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>Great-circle distance in metres between two points (haversine formula; good to about 0.5%).</summary>
    public static double Meters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;

        var dLat = Radians(latitude2 - latitude1);
        var dLon = Radians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(Radians(latitude1)) * Math.Cos(Radians(latitude2)) * Math.Pow(Math.Sin(dLon / 2), 2);

        // Rounding can push a just above 1 for antipodal points.
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
