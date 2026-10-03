using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Mapping;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for street lamps: list them near a point, and summarise coverage and technology.</summary>
public sealed class StreetLightsQueryService(IReadRepository<StreetLight> lights)
{
    public const int MaxLimit = 30000;

    /// <summary>Lamps within <paramref name="radiusMeters"/> of <paramref name="near"/> (nearest first), or all lamps.</summary>
    public async Task<IReadOnlyList<StreetLightDto>> GetAsync(
        GeoPoint? near, double radiusMeters, StreetLightTechnology? technology, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var spec = technology is null ? null : new Specification<StreetLight>(l => l.Technology == technology);
        var items = await lights.ListAsync(spec, ct);

        if (near is not { } point)
        {
            return items.Take(limit).Select(l => l.ToDto()).ToList();
        }

        return items
            .Select(l => (Light: l, Distance: point.DistanceTo(l.Location)))
            .Where(x => x.Distance <= radiusMeters)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => x.Light.ToDto(x.Distance))
            .ToList();
    }

    /// <summary>Counts by technology and mount; with a point, also lamps per km² in that circle.</summary>
    public async Task<StreetLightSummaryDto> GetSummaryAsync(GeoPoint? near, double radiusMeters, CancellationToken ct = default)
    {
        var items = (IEnumerable<StreetLight>)await lights.ListAsync(cancellationToken: ct);
        double? area = null;
        if (near is { } point)
        {
            items = items.Where(l => point.DistanceTo(l.Location) <= radiusMeters);
            area = Math.PI * Math.Pow(radiusMeters / 1000.0, 2);
        }

        var list = items.ToList();
        return new StreetLightSummaryDto(
            list.Count,
            list.Count(l => l.Technology != StreetLightTechnology.Unknown),
            area is null ? null : Math.Round(area.Value, 3),
            area is null or 0 ? null : Math.Round(list.Count / area.Value, 1),
            Count(list.Select(l => l.Technology.ToString())),
            Count(list.Select(l => l.Mount ?? "unknown")),
            list.FirstOrDefault()?.Source ?? "OpenStreetMap");
    }

    private static IReadOnlyDictionary<string, int> Count(IEnumerable<string> values) =>
        values.GroupBy(v => v).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
}
