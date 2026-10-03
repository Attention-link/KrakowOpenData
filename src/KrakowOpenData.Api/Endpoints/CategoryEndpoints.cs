using KrakowOpenData.Application.Services;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>One extension method per data category keeps routes discoverable and easy to prune.</summary>
public static class CategoryEndpoints
{
    // ── Catalog ──────────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/catalog").WithTags("Catalog");

        g.MapGet("/", (CatalogService catalog) => Results.Ok(catalog.GetCategories()));

        g.MapGet("/{key}", (string key, CatalogService catalog) =>
            catalog.GetDataset(key) is { } dataset ? Results.Ok(dataset) : Results.NotFound());

        return api;
    }

    // ── Mobility ─────────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapMobilityEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/mobility").WithTags(nameof(DataCategory.Mobility));

        g.MapGet("/stops", async (string? q, int? page, int? pageSize, TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchStopsAsync(q, page ?? 1, pageSize ?? 50, ct)));

        g.MapGet("/stops/nearby", async (double lat, double lon, double? radius, int? limit, TransitQueryService svc, CancellationToken ct) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!point.IsValid) return Results.ValidationProblem(new Dictionary<string, string[]> { ["lat,lon"] = ["Invalid coordinates."] });
            return Results.Ok(await svc.NearbyStopsAsync(point, radius ?? 500, limit ?? 20, ct));
        });

        g.MapGet("/stops/{stopId}", async (string stopId, TransitQueryService svc, CancellationToken ct) =>
            await svc.GetStopAsync(stopId, ct) is { } stop ? Results.Ok(stop) : Results.NotFound());

        g.MapGet("/stops/{stopId}/departures", async (string stopId, int? limit, int? windowMinutes, TransitQueryService svc, CancellationToken ct) =>
        {
            if (await svc.GetStopAsync(stopId, ct) is null) return Results.NotFound();
            var window = TimeSpan.FromMinutes(Math.Clamp(windowMinutes ?? 60, 1, 24 * 60));
            return Results.Ok(await svc.GetDeparturesAsync(stopId, limit ?? 10, window, ct));
        });

        g.MapGet("/routes", async (string? mode, TransitQueryService svc, CancellationToken ct) =>
        {
            TransportMode? parsed = null;
            if (!string.IsNullOrWhiteSpace(mode))
            {
                if (!Enum.TryParse<TransportMode>(mode, ignoreCase: true, out var m))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = [$"Use one of: {string.Join(", ", Enum.GetNames<TransportMode>())}."] });
                parsed = m;
            }

            return Results.Ok(await svc.GetRoutesAsync(parsed, ct));
        });

        g.MapGet("/vehicles", async (string? routeId, TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetVehiclesAsync(routeId, ct)));

        g.MapGet("/trip-updates", async (int? minDelaySeconds, TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTripUpdatesAsync(minDelaySeconds, ct)));

        g.MapGet("/alerts", async (TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetActiveAlertsAsync(ct)));

        g.MapGet("/park-and-ride", async (TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetParkAndRideAsync(ct)));

        return api;
    }

    // ── Environment ──────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapEnvironmentEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/environment").WithTags(nameof(DataCategory.Environment));

        g.MapGet("/weather", async (EnvironmentQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetWeatherAsync(ct)));

        g.MapGet("/air-quality", async (EnvironmentQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAirQualityAsync(ct)));

        return api;
    }

    // ── Climate & crisis ─────────────────────────────────────────────────────
    public static RouteGroupBuilder MapClimateCrisisEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/crisis").WithTags(nameof(DataCategory.ClimateAndCrisis));

        g.MapGet("/river-gauges", async (bool? elevatedOnly, ClimateCrisisQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRiverGaugesAsync(elevatedOnly ?? false, ct)));

        g.MapGet("/warnings", async (string? teryt, ClimateCrisisQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetActiveWarningsAsync(teryt, ct)));

        return api;
    }

    // ── Urban space ──────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapUrbanSpaceEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/urban").WithTags(nameof(DataCategory.UrbanSpace));

        g.MapGet("/districts", async (UrbanSpaceQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDistrictsAsync(ct)));

        g.MapGet("/districts/{id}", async (string id, UrbanSpaceQueryService svc, CancellationToken ct) =>
            await svc.GetDistrictAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        return api;
    }

    // ── Public services ──────────────────────────────────────────────────────
    public static RouteGroupBuilder MapPublicServicesEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/services").WithTags(nameof(DataCategory.PublicServices));

        g.MapGet("/cards", async (string? q, int? limit, PublicServicesQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(q, limit ?? 20, ct)));

        g.MapGet("/cards/{id}", async (string id, PublicServicesQueryService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } card ? Results.Ok(card) : Results.NotFound());

        // NFZ waiting lists in Kraków, e.g. /api/services/waiting-lists?benefit=ortoped&urgent=false
        g.MapGet("/waiting-lists", async (string? benefit, bool? urgent, WaitingListQueryService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(benefit) || benefit.Trim().Length < 3)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["benefit"] = ["Give at least 3 letters of the service name, e.g. 'ortoped'."] });
            return Results.Ok(await svc.SearchAsync(benefit, urgent ?? false, ct));
        });

        return api;
    }

    // ── City Open Data portal (generic tables, mostly Society) ───────────────
    public static RouteGroupBuilder MapOpenDataPortalEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/open-data").WithTags("OpenDataPortal");

        g.MapGet("/tables", (string? category, OpenDataPortalService svc) =>
        {
            DataCategory? parsed = null;
            if (!string.IsNullOrWhiteSpace(category))
            {
                if (!Enum.TryParse<DataCategory>(category, ignoreCase: true, out var c))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["category"] = [$"Use one of: {string.Join(", ", Enum.GetNames<DataCategory>())}."] });
                parsed = c;
            }

            return Results.Ok(svc.ListTables(parsed));
        });

        g.MapGet("/tables/{key}", async (string key, int? limit, string? q, OpenDataPortalService svc, CancellationToken ct) =>
            await svc.GetRowsAsync(key, limit, q, ct) is { } rows ? Results.Ok(rows) : Results.NotFound());

        return api;
    }
}
