using KrakowOpenData.Domain.Accessibility;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Accessibility;

/// <summary>
/// A simple grid index (50 m buckets) over access items so "what is within 15 m of this route" and "what is within 1 km
/// of this point" do not scan tens of thousands of path segments. Lines are indexed by segment.
/// Distances use a local flat projection (error well under 1 % inside Kraków).
/// </summary>
public sealed class AccessIndex
{
    private const double BucketMeters = 50;
    private const double MetersPerDegreeLatitude = 111_320;
    private static readonly double MetersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(50.06 * Math.PI / 180);

    private readonly Dictionary<(int X, int Y), List<int>> _buckets = new();

    public IReadOnlyList<AccessFeature> Features { get; }

    /// <summary>Bounding box of all items (min lat, min lon, max lat, max lon); null when empty.</summary>
    public (double MinLat, double MinLon, double MaxLat, double MaxLon)? Bounds { get; }

    public AccessIndex(IReadOnlyList<AccessFeature> features)
    {
        Features = features;
        double minLat = double.MaxValue, minLon = double.MaxValue, maxLat = double.MinValue, maxLon = double.MinValue;
        for (var i = 0; i < features.Count; i++)
        {
            var f = features[i];
            // Only points decide the data area: roads crossing the download box carry geometry far outside it.
            if (f.Line is null)
            {
                minLat = Math.Min(minLat, f.Location.Latitude); maxLat = Math.Max(maxLat, f.Location.Latitude);
                minLon = Math.Min(minLon, f.Location.Longitude); maxLon = Math.Max(maxLon, f.Location.Longitude);
            }

            if (f.Line is { Count: >= 2 } line)
            {
                var keys = new HashSet<(int, int)>();
                for (var s = 1; s < line.Count; s++)
                {
                    var (ax, ay) = Project(line[s - 1]);
                    var (bx, by) = Project(line[s]);
                    for (var x = Bucket(Math.Min(ax, bx)); x <= Bucket(Math.Max(ax, bx)); x++)
                    for (var y = Bucket(Math.Min(ay, by)); y <= Bucket(Math.Max(ay, by)); y++)
                        keys.Add((x, y));
                }

                foreach (var k in keys) Add(k, i);
            }
            else
            {
                var (x, y) = Project(f.Location);
                Add((Bucket(x), Bucket(y)), i);
            }
        }

        Bounds = minLat == double.MaxValue ? null : (minLat, minLon, maxLat, maxLon);
    }

    public bool Covers(GeoPoint p) =>
        Bounds is { } b && p.Latitude >= b.MinLat && p.Latitude <= b.MaxLat && p.Longitude >= b.MinLon && p.Longitude <= b.MaxLon;

    /// <summary>Items within <paramref name="radius"/> metres of a point, with their distance (to the nearest segment for lines).</summary>
    public IEnumerable<(AccessFeature Feature, double Distance)> Near(GeoPoint p, double radius)
    {
        var (px, py) = Project(p);
        var seen = new HashSet<int>();
        for (var x = Bucket(px - radius); x <= Bucket(px + radius); x++)
        for (var y = Bucket(py - radius); y <= Bucket(py + radius); y++)
        {
            if (!_buckets.TryGetValue((x, y), out var list)) continue;
            foreach (var i in list)
            {
                if (!seen.Add(i)) continue;
                var d = Distance(Features[i], p);
                if (d <= radius) yield return (Features[i], d);
            }
        }
    }

    /// <summary>Distance in metres from a point to an item (to the nearest segment for steps and paths).</summary>
    public static double Distance(AccessFeature f, GeoPoint p)
    {
        if (f.Line is not { Count: >= 2 } line) return Flat(f.Location, p);
        var (px, py) = Project(p);
        var best = double.MaxValue;
        for (var s = 1; s < line.Count; s++)
        {
            var (ax, ay) = Project(line[s - 1]);
            var (bx, by) = Project(line[s]);
            best = Math.Min(best, PointSegment(px, py, ax, ay, bx, by));
        }

        return best;
    }

    /// <summary>Length of a line in metres.</summary>
    public static double Length(IReadOnlyList<GeoPoint> line)
    {
        var total = 0.0;
        for (var i = 1; i < line.Count; i++) total += Flat(line[i - 1], line[i]);
        return total;
    }

    public static double Flat(GeoPoint a, GeoPoint b)
    {
        var dy = (b.Latitude - a.Latitude) * MetersPerDegreeLatitude;
        var dx = (b.Longitude - a.Longitude) * MetersPerDegreeLongitude;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double PointSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var ll = dx * dx + dy * dy;
        var t = ll == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / ll, 0, 1);
        var ex = px - ax - t * dx;
        var ey = py - ay - t * dy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    private static (double X, double Y) Project(GeoPoint p) => (p.Longitude * MetersPerDegreeLongitude, p.Latitude * MetersPerDegreeLatitude);

    private static int Bucket(double meters) => (int)Math.Floor(meters / BucketMeters);

    private void Add((int, int) key, int index)
    {
        if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = [];
        list.Add(index);
    }
}
