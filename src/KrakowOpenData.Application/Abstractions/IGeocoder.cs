using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Abstractions;

/// <summary>Turns an address or place name into coordinates (search, for autocomplete) and coordinates into an address (reverse).</summary>
public interface IGeocoder
{
    /// <summary>Places in and around Kraków matching <paramref name="query"/>, best first. <paramref name="near"/> biases the ranking.</summary>
    Task<IReadOnlyList<GeocodeResultDto>> SearchAsync(string query, GeoPoint? near, int limit, CancellationToken ct = default);

    /// <summary>The address or named place nearest to a point (within about 300 m), or null.</summary>
    Task<GeocodeResultDto?> ReverseAsync(GeoPoint point, CancellationToken ct = default);
}
