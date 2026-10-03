using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Safety;

/// <summary>
/// The score grid: square cells of about 250 m, numbered by (row, col) from a fixed south-west origin.
/// Row grows northwards, col eastwards. The cell id is "row-col". Cell size in degrees uses the latitude of
/// Kraków (50.06° N) so cells are roughly square on the ground.
/// </summary>
public static class GridSpec
{
    public const double CellSizeMeters = 250;
    public const double OriginLatitude = 49.90;
    public const double OriginLongitude = 19.70;

    /// <summary>Latitude and longitude limits accepted for reports and alerts (generously around Kraków).</summary>
    public const double MinLatitude = 49.90, MaxLatitude = 50.20, MinLongitude = 19.70, MaxLongitude = 20.30;

    private const double MetersPerDegreeLatitude = 111_320;
    private const double ReferenceLatitude = 50.06;

    public static readonly double CellLatitudeDegrees = CellSizeMeters / MetersPerDegreeLatitude;

    public static readonly double CellLongitudeDegrees =
        CellSizeMeters / (MetersPerDegreeLatitude * Math.Cos(ReferenceLatitude * Math.PI / 180));

    public static (int Row, int Col) CellOf(GeoPoint p) =>
        ((int)Math.Floor((p.Latitude - OriginLatitude) / CellLatitudeDegrees),
         (int)Math.Floor((p.Longitude - OriginLongitude) / CellLongitudeDegrees));

    public static GeoPoint CenterOf(int row, int col) =>
        new(OriginLatitude + (row + 0.5) * CellLatitudeDegrees, OriginLongitude + (col + 0.5) * CellLongitudeDegrees);

    public static string IdOf(int row, int col) => $"{row}-{col}";

    public static string IdOf(GeoPoint p)
    {
        var (row, col) = CellOf(p);
        return IdOf(row, col);
    }

    public static bool TryParseId(string? id, out int row, out int col)
    {
        row = col = 0;
        var parts = id?.Split('-');
        return parts is { Length: 2 } && int.TryParse(parts[0], out row) && int.TryParse(parts[1], out col);
    }

    public static bool IsInArea(GeoPoint p) =>
        p.IsValid && p.Latitude is >= MinLatitude and <= MaxLatitude && p.Longitude is >= MinLongitude and <= MaxLongitude;

    private static readonly double MetersPerDegreeLongitude =
        MetersPerDegreeLatitude * Math.Cos(ReferenceLatitude * Math.PI / 180);

    /// <summary>
    /// Fast straight-line distance in metres (flat-earth approximation with a fixed longitude scale for 50.06° N;
    /// error under 0.5% anywhere inside Kraków). Used for the millions of distance checks when scoring the grid.
    /// </summary>
    public static double Distance(GeoPoint a, GeoPoint b)
    {
        var dy = (b.Latitude - a.Latitude) * MetersPerDegreeLatitude;
        var dx = (b.Longitude - a.Longitude) * MetersPerDegreeLongitude;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
