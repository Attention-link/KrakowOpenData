using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Safety;

/// <summary>Geometry helpers for walking paths (polylines of points).</summary>
public static class PathSampler
{
    /// <summary>Length of a polyline in metres.</summary>
    public static double Length(IReadOnlyList<GeoPoint> path)
    {
        var total = 0.0;
        for (var i = 1; i < path.Count; i++) total += GridSpec.Distance(path[i - 1], path[i]);
        return total;
    }

    /// <summary>
    /// Points along the polyline every <paramref name="stepMeters"/> of walking, always including the first and last point.
    /// The step is stretched slightly so the samples divide the path evenly.
    /// </summary>
    public static IReadOnlyList<GeoPoint> Sample(IReadOnlyList<GeoPoint> path, double stepMeters)
    {
        if (path.Count == 0) return [];
        if (path.Count == 1) return [path[0]];

        var length = Length(path);
        var steps = Math.Max(1, (int)Math.Ceiling(length / stepMeters));
        var result = new List<GeoPoint>(steps + 1) { path[0] };

        var segment = 1;
        var walked = 0.0;      // distance from the start to path[segment - 1]
        for (var i = 1; i < steps; i++)
        {
            var target = length * i / steps;
            while (segment < path.Count - 1 && walked + GridSpec.Distance(path[segment - 1], path[segment]) < target)
            {
                walked += GridSpec.Distance(path[segment - 1], path[segment]);
                segment++;
            }

            var a = path[segment - 1];
            var b = path[segment];
            var len = GridSpec.Distance(a, b);
            var f = len <= 0 ? 0 : Math.Clamp((target - walked) / len, 0, 1);
            result.Add(new GeoPoint(a.Latitude + (b.Latitude - a.Latitude) * f, a.Longitude + (b.Longitude - a.Longitude) * f));
        }

        result.Add(path[^1]);
        return result;
    }
}
