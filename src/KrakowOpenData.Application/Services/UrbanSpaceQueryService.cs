using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Contracts;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for districts and spatial data (Urban space category).</summary>
public sealed class UrbanSpaceQueryService(IReadRepository<District> districts)
{
    public async Task<IReadOnlyList<DistrictDto>> GetDistrictsAsync(CancellationToken ct = default) =>
        (await districts.ListAsync(cancellationToken: ct))
            .OrderBy(d => d.Number)
            .Select(d => d.ToDto())
            .ToList();

    public async Task<DistrictDto?> GetDistrictAsync(string id, CancellationToken ct = default) =>
        (await districts.GetByIdAsync(id, ct))?.ToDto();
}
