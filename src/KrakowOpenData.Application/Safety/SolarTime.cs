namespace KrakowOpenData.Application.Safety;

/// <summary>
/// Sunrise and sunset from the standard low-precision solar position formula (the NOAA / Almanac method), good to a
/// few minutes. Used to tell the app whether it is dark in Kraków, so it can suggest the night-safety view.
/// </summary>
public static class SolarTime
{
    /// <summary>Sun 0.833° below the horizon (refraction + solar disc), the usual definition of sunrise/sunset.</summary>
    private const double ZenithDegrees = 90.833;

    public static (DateTimeOffset? Sunrise, DateTimeOffset? Sunset) SunTimes(DateOnly date, double latitude, double longitude)
    {
        return (Event(date, latitude, longitude, rising: true), Event(date, latitude, longitude, rising: false));
    }

    public static bool IsDark(DateTimeOffset moment, double latitude, double longitude)
    {
        var date = DateOnly.FromDateTime(moment.UtcDateTime);
        var (rise, set) = SunTimes(date, latitude, longitude);
        if (rise is null || set is null) return false;
        var utc = moment.ToUniversalTime();
        // Sunrise and sunset are computed for the UTC date; at Kraków's longitude both fall inside it.
        return utc < rise.Value || utc > set.Value;
    }

    private static DateTimeOffset? Event(DateOnly date, double latitude, double longitude, bool rising)
    {
        var n = date.DayOfYear;
        var lngHour = longitude / 15.0;
        var t = n + ((rising ? 6 : 18) - lngHour) / 24.0;

        var meanAnomaly = 0.9856 * t - 3.289;
        var trueLongitude = Normalize(meanAnomaly + 1.916 * Sin(meanAnomaly) + 0.020 * Sin(2 * meanAnomaly) + 282.634, 360);

        var rightAscension = Normalize(Atan(0.91764 * Tan(trueLongitude)), 360);
        rightAscension += Math.Floor(trueLongitude / 90) * 90 - Math.Floor(rightAscension / 90) * 90;
        rightAscension /= 15.0;

        var sinDec = 0.39782 * Sin(trueLongitude);
        var cosDec = Math.Cos(Math.Asin(sinDec));

        var cosHour = (Math.Cos(Rad(ZenithDegrees)) - sinDec * Sin(latitude)) / (cosDec * Cos(latitude));
        if (cosHour is > 1 or < -1) return null; // polar day or night; not relevant for Kraków

        var hour = rising ? 360 - Deg(Math.Acos(cosHour)) : Deg(Math.Acos(cosHour));
        hour /= 15.0;

        var localMeanTime = hour + rightAscension - 0.06571 * t - 6.622;
        var utcHours = Normalize(localMeanTime - lngHour, 24);

        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero).AddHours(utcHours);
    }

    private static double Normalize(double value, double range)
    {
        var v = value % range;
        return v < 0 ? v + range : v;
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180;
    private static double Deg(double radians) => radians * 180 / Math.PI;
    private static double Sin(double degrees) => Math.Sin(Rad(degrees));
    private static double Cos(double degrees) => Math.Cos(Rad(degrees));
    private static double Tan(double degrees) => Math.Tan(Rad(degrees));
    private static double Atan(double value) => Deg(Math.Atan(value));
}
