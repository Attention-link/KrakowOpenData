using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Mapping;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for districts and public amenities (Urban space category).</summary>
public sealed class UrbanSpaceQueryService(IReadRepository<District> districts, IReadRepository<Amenity> amenities)
{
    public async Task<IReadOnlyList<DistrictDto>> GetDistrictsAsync(CancellationToken ct = default) =>
        (await districts.ListAsync(cancellationToken: ct))
            .OrderBy(d => d.Number)
            .Select(d => d.ToDto())
            .ToList();

    public async Task<DistrictDto?> GetDistrictAsync(string id, CancellationToken ct = default) =>
        (await districts.GetByIdAsync(id, ct))?.ToDto();

    /// <summary>
    /// Amenities of one kind (or all). With <paramref name="near"/>, only those within
    /// <paramref name="radiusMeters"/>, nearest first.
    /// </summary>
    public async Task<IReadOnlyList<AmenityDto>> GetAmenitiesAsync(
        AmenityKind? kind, GeoPoint? near, double radiusMeters, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 5000);
        var spec = kind is null ? null : new Specification<Amenity>(a => a.Kind == kind);
        var items = await amenities.ListAsync(spec, ct);

        if (near is { } point)
        {
            return items
                .Select(a => (Amenity: a, Distance: point.DistanceTo(a.Location)))
                .Where(x => x.Distance <= radiusMeters)
                .OrderBy(x => x.Distance)
                .Take(limit)
                .Select(x => x.Amenity.ToDto(x.Distance))
                .ToList();
        }

        return items
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Name ?? "~", StringComparer.CurrentCulture)
            .Take(limit)
            .Select(a => a.ToDto())
            .ToList();
    }
}
