using System.Globalization;

namespace KrakowOpenData.Domain.Common;

/// <summary>A WGS84 coordinate.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude)
{
    private const double EarthRadiusMeters = 6_371_000d;

    public bool IsValid =>
        Latitude is >= -90 and <= 90 &&
        Longitude is >= -180 and <= 180 &&
        !(Latitude == 0 && Longitude == 0);

    /// <summary>Great-circle distance in metres (haversine formula).</summary>
    public double DistanceTo(GeoPoint other)
    {
        static double ToRad(double deg) => deg * Math.PI / 180d;

        var dLat = ToRad(other.Latitude - Latitude);
        var dLon = ToRad(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(ToRad(Latitude)) * Math.Cos(ToRad(other.Latitude)) *
                Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    /// <summary>Parses "lat,lon" using invariant culture.</summary>
    public static bool TryParse(string? value, out GeoPoint point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return false;
        }

        point = new GeoPoint(lat, lon);
        return point.IsValid;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.######},{Longitude:0.######}");
}
