using KrakowOpenData.Application.Accessibility;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>
/// "Dostępność / Kraków bez barier": concrete barriers and amenities (steps, kerbs, ramps, lifts, entrances, surfaces, slopes,
/// accessible toilets, benches, tactile paving) for wheelchair users, people with prams and people with limited mobility.
/// Mapped under <c>/api/safety/access</c> by <see cref="SafetyEndpoints"/>; validation errors become 400 there.
/// </summary>
public static class AccessEndpoints
{
    public static RouteGroupBuilder MapAccessEndpoints(this RouteGroupBuilder g)
    {
        g.MapGet("/access", async (double lat, double lon, double? radius, string? profile, string? kinds, int? limit, AccessService svc, CancellationToken ct) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!GridSpec.IsInArea(point)) return Results.ValidationProblem(Problem("lat,lon", "The location must be in or around Kraków."));
            if (AccessProfile.Parse(profile) is not { } p) return Results.ValidationProblem(Problem("profile", "Use wheelchair, pram or mobility."));
            return Results.Ok(await svc.NearAsync(point, radius ?? 500, p, AccessService.ParseKinds(kinds), limit ?? AccessService.DefaultLimit, ct));
        })
            .WithName("GetAccessNearby")
            .WithSummary("Barriers and amenities within radius (≤ 1000 m) of a point, nearest first, for a profile (wheelchair | pram | mobility; only changes what counts as a barrier). " +
                         "kinds = comma list of steps, kerb, elevator, entrance, place, toilets, bench, tactile, path. Every item has facts, source, last OSM edit and reliability; " +
                         "counts, coverage (\"x of y entrances have data\") and a data note when coverage is thin. Unknown is never accessible.")
            .Produces<AccessNearbyDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/access/route", async (string from, string to, string? profile, AccessService svc, CancellationToken ct) =>
        {
            if (!GeoPoint.TryParse(from, out var a) || !GeoPoint.TryParse(to, out var b) || !GridSpec.IsInArea(a) || !GridSpec.IsInArea(b))
                return Results.ValidationProblem(Problem("from,to", "Give from and to as \"lat,lon\" inside Kraków."));
            if (AccessProfile.Parse(profile) is not { } p) return Results.ValidationProblem(Problem("profile", "Use wheelchair, pram or mobility."));
            return Results.Ok(await svc.AssessRouteAsync(a, b, p, ct));
        })
            .WithName("GetAccessRoute")
            .WithSummary("Barrier summary of the fastest walking route from A to B for a profile: steps without a ramp, raised kerbs, metres of rough or unpaved surface and of steep slope, " +
                         "and the share of the route with no data (never counted as accessible). Items within 15 m of points sampled every 10 m.")
            .Produces<AccessRouteDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapPost("/access/route", async (AccessRouteRequest request, AccessService svc, CancellationToken ct) =>
        {
            if (AccessProfile.Parse(request.Profile) is not { } p) return Results.ValidationProblem(Problem("profile", "Use wheelchair, pram or mobility."));
            if (request.Path is null || request.Path.Any(x => x is not { Length: 2 }))
                return Results.ValidationProblem(Problem("path", "Give the path as [lat, lon] pairs."));
            var path = request.Path.Select(x => new GeoPoint(x[0], x[1])).ToList();
            return Results.Ok(await svc.AssessPathAsync(path, p, ct));
        })
            .WithName("AssessAccessPath")
            .WithSummary("The same barrier summary for a path you already have (e.g. the fastest or safer route from GET /api/safety/route), given as [lat, lon] pairs.")
            .Produces<AccessRouteDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return g;
    }

    private static Dictionary<string, string[]> Problem(string field, string message) => new() { [field] = [message] };
}
