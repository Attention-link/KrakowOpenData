using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Api.Tests.Fakes;

/// <summary>Address search without the network: two known places.</summary>
public sealed class FakeGeocoder : IGeocoder
{
    private static readonly GeocodeResultDto Rynek = new("Rynek Główny, Stare Miasto", "Rynek Główny", "Rynek Główny", null, "Stare Miasto", "31-042", 50.0615, 19.9371, "locality");
    private static readonly GeocodeResultDto Wawel = new("Wawel 5, Stare Miasto", "Wawel", "Wawel", "5", "Stare Miasto", "31-001", 50.0540, 19.9354, "house");

    public Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, GeoPoint? near, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GeocodeResultDto>>(new[] { Rynek, Wawel }
            .Where(r => r.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList());

    public Task<GeocodeResultDto?> ReverseAsync(GeoPoint point, CancellationToken ct = default) =>
        Task.FromResult<GeocodeResultDto?>(point.DistanceTo(new GeoPoint(Rynek.Latitude, Rynek.Longitude)) < 300 ? Rynek : null);
}
