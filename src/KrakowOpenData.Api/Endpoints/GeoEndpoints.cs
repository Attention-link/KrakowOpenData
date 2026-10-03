using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>Address search with autocomplete and "what address is this point?", for the address boxes and map labels.</summary>
public static class GeoEndpoints
{
    public static RouteGroupBuilder MapGeoEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/geo").WithTags("Geocoding");

        // e.g. /api/geo/search?q=rynek%20gl&lat=50.06&lon=19.94
        g.MapGet("/search", async (string q, double? lat, double? lon, int? limit, IGeocoder geocoder, CancellationToken ct) =>
        {
            q = (q ?? string.Empty).Trim();
            if (q.Length is < 2 or > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Type 2 to 100 characters."] });

            GeoPoint? near = lat is not null && lon is not null && new GeoPoint(lat.Value, lon.Value).IsValid ? new GeoPoint(lat.Value, lon.Value) : null;
            return Results.Ok(await geocoder.SearchAsync(q, near, limit ?? 6, ct));
        })
            .WithName("SearchAddress")
            .WithSummary("Addresses and places in Kraków matching the text (search as you type). Powered by OpenStreetMap via Photon.")
            .Produces<IReadOnlyList<GeocodeResultDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/reverse", async (double lat, double lon, IGeocoder geocoder, CancellationToken ct) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!GridSpec.IsInArea(point))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["lat,lon"] = ["The location must be in or around Kraków."] });
            return await geocoder.ReverseAsync(point, ct) is { } r ? Results.Ok(r) : Results.NotFound();
        })
            .WithName("ReverseGeocode")
            .WithSummary("The address or named place nearest to a point (404 when nothing is mapped within about 300 m).")
            .Produces<GeocodeResultDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return api;
    }
}
