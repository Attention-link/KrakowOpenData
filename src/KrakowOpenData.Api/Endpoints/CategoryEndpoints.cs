using KrakowOpenData.Application.Services;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>
/// One extension method per data category keeps routes discoverable and easy to prune. Every endpoint
/// declares its response types for OpenAPI; 503 is returned by UpstreamUnavailableFilter when a public source is down.
/// </summary>
public static class CategoryEndpoints
{
    // ── Catalog ──────────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapCatalogEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/catalog").WithTags("Catalog");

        g.MapGet("/", (CatalogService catalog) => Results.Ok(catalog.GetCategories()))
            .WithName("GetCatalog")
            .WithSummary("Every dataset, grouped by category, with its source and status.")
            .Produces<IReadOnlyList<CategoryDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/{key}", (string key, CatalogService catalog) =>
            catalog.GetDataset(key) is { } dataset ? Results.Ok(dataset) : Results.NotFound())
            .WithName("GetDataset")
            .WithSummary("One dataset by key.")
            .Produces<DatasetDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    // ── Mobility ─────────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapMobilityEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/mobility").WithTags(nameof(DataCategory.Mobility));

        g.MapGet("/stops", async (string? q, int? page, int? pageSize, TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchStopsAsync(q, page ?? 1, pageSize ?? 50, ct)))
            .WithName("SearchStops")
            .WithSummary("Search stops by name (diacritics ignored), paged.")
            .Produces<PagedResult<StopDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/stops/nearby", async (double lat, double lon, double? radius, int? limit, TransitQueryService svc, CancellationToken ct) =>
        {
            var point = new GeoPoint(lat, lon);
            if (!point.IsValid) return Results.ValidationProblem(new Dictionary<string, string[]> { ["lat,lon"] = ["Invalid coordinates."] });
            return Results.Ok(await svc.NearbyStopsAsync(point, radius ?? 500, limit ?? 20, ct));
        })
            .WithName("GetNearbyStops")
            .WithSummary("Stops near a point, nearest first.")
            .Produces<IReadOnlyList<StopDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        g.MapGet("/stops/{stopId}", async (string stopId, TransitQueryService svc, CancellationToken ct) =>
            await svc.GetStopAsync(stopId, ct) is { } stop ? Results.Ok(stop) : Results.NotFound())
            .WithName("GetStop")
            .WithSummary("One stop by id (e.g. T:123).")
            .Produces<StopDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/stops/{stopId}/departures", async (string stopId, int? limit, int? windowMinutes, TransitQueryService svc, CancellationToken ct) =>
        {
            if (await svc.GetStopAsync(stopId, ct) is null) return Results.NotFound();
            var window = TimeSpan.FromMinutes(Math.Clamp(windowMinutes ?? 60, 1, 24 * 60));
            return Results.Ok(await svc.GetDeparturesAsync(stopId, limit ?? 10, window, ct));
        })
            .WithName("GetDepartures")
            .WithSummary("Next departures from a stop: timetable plus live delay.")
            .Produces<IReadOnlyList<DepartureDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
        })
            .WithName("GetRoutes")
            .WithSummary("Lines, optionally filtered by mode (Tram, Bus, Rail, Metro, Other).")
            .Produces<IReadOnlyList<RouteDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        g.MapGet("/vehicles", async (string? routeId, TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetVehiclesAsync(routeId, ct)))
            .WithName("GetVehicles")
            .WithSummary("Live vehicle positions, optionally for one route.")
            .Produces<IReadOnlyList<VehicleDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/trip-updates", async (int? minDelaySeconds, TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetTripUpdatesAsync(minDelaySeconds, ct)))
            .WithName("GetTripUpdates")
            .WithSummary("Live trip delays.")
            .Produces<IReadOnlyList<TripUpdateDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/alerts", async (TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetActiveAlertsAsync(ct)))
            .WithName("GetAlerts")
            .WithSummary("Active disruptions and detours.")
            .Produces<IReadOnlyList<AlertDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/park-and-ride", async (TransitQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetParkAndRideAsync(ct)))
            .WithName("GetParkAndRide")
            .WithSummary("P+R car parks from OpenStreetMap.")
            .Produces<IReadOnlyList<ParkAndRideDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return api;
    }

    // ── Environment ──────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapEnvironmentEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/environment").WithTags(nameof(DataCategory.Environment));

        g.MapGet("/weather", async (EnvironmentQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetWeatherAsync(ct)))
            .WithName("GetWeather")
            .WithSummary("Latest IMGW synoptic reading for Kraków.")
            .Produces<IReadOnlyList<WeatherDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/air-quality", async (EnvironmentQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAirQualityAsync(ct)))
            .WithName("GetAirQuality")
            .WithSummary("Latest PM2.5, PM10, NO2 and Polish index for every GIOŚ station in Kraków.")
            .Produces<IReadOnlyList<AirQualityDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return api;
    }

    // ── Climate & crisis ─────────────────────────────────────────────────────
    public static RouteGroupBuilder MapClimateCrisisEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/crisis").WithTags(nameof(DataCategory.ClimateAndCrisis));

        g.MapGet("/river-gauges", async (bool? elevatedOnly, ClimateCrisisQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRiverGaugesAsync(elevatedOnly ?? false, ct)))
            .WithName("GetRiverGauges")
            .WithSummary("IMGW river gauges around Kraków with warning and alarm levels.")
            .Produces<IReadOnlyList<RiverGaugeDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/warnings", async (string? teryt, ClimateCrisisQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetActiveWarningsAsync(teryt, ct)))
            .WithName("GetWarnings")
            .WithSummary("Active IMGW weather and hydrological warnings for an area (default Kraków, TERYT 1261).")
            .Produces<IReadOnlyList<WarningDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return api;
    }

    // ── Urban space ──────────────────────────────────────────────────────────
    public static RouteGroupBuilder MapUrbanSpaceEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/urban").WithTags(nameof(DataCategory.UrbanSpace));

        g.MapGet("/districts", async (UrbanSpaceQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDistrictsAsync(ct)))
            .WithName("GetDistricts")
            .WithSummary("The 18 districts with registered population.")
            .Produces<IReadOnlyList<DistrictDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/districts/{id}", async (string id, UrbanSpaceQueryService svc, CancellationToken ct) =>
            await svc.GetDistrictAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .WithName("GetDistrict")
            .WithSummary("One district by Roman numeral (e.g. XIV).")
            .Produces<DistrictDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // e.g. /api/urban/amenities?kind=Defibrillator&lat=50.0617&lon=19.9373&radius=1000
        g.MapGet("/amenities", async (string? kind, double? lat, double? lon, double? radius, int? limit,
            UrbanSpaceQueryService svc, CancellationToken ct) =>
        {
            AmenityKind? parsed = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<AmenityKind>(kind, ignoreCase: true, out var k))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = [$"Use one of: {string.Join(", ", Enum.GetNames<AmenityKind>())}."] });
                parsed = k;
            }

            GeoPoint? near = null;
            if (lat is not null || lon is not null)
            {
                var point = new GeoPoint(lat ?? double.NaN, lon ?? double.NaN);
                if (lat is null || lon is null || !point.IsValid)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["lat,lon"] = ["Give both lat and lon as valid coordinates."] });
                near = point;
            }

            return Results.Ok(await svc.GetAmenitiesAsync(parsed, near, radius ?? 1000, limit ?? 500, ct));
        })
            .WithName("GetAmenities")
            .WithSummary("AEDs, drinking water, toilets, EV chargers and bike parking from OpenStreetMap, optionally near a point.")
            .Produces<IReadOnlyList<AmenityDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        // e.g. /api/urban/street-lights?lat=50.0617&lon=19.9373&radius=300&technology=Led
        g.MapGet("/street-lights", async (double? lat, double? lon, double? radius, string? technology, int? limit,
            StreetLightsQueryService svc, CancellationToken ct) =>
        {
            if (!TryReadPoint(lat, lon, out var near, out var pointError)) return pointError!;

            StreetLightTechnology? parsed = null;
            if (!string.IsNullOrWhiteSpace(technology))
            {
                if (!Enum.TryParse<StreetLightTechnology>(technology, ignoreCase: true, out var t))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["technology"] = [$"Use one of: {string.Join(", ", Enum.GetNames<StreetLightTechnology>())}."] });
                parsed = t;
            }

            return Results.Ok(await svc.GetAsync(near, radius ?? 500, parsed, limit ?? (near is null ? 1000 : 2000), ct));
        })
            .WithName("GetStreetLights")
            .WithSummary("Street lamps from OpenStreetMap (~27,000 in Kraków): near a point (nearest first) or citywide, optionally by technology.")
            .Produces<IReadOnlyList<StreetLightDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        g.MapGet("/street-lights/summary", async (double? lat, double? lon, double? radius, StreetLightsQueryService svc, CancellationToken ct) =>
        {
            if (!TryReadPoint(lat, lon, out var near, out var pointError)) return pointError!;
            return Results.Ok(await svc.GetSummaryAsync(near, radius ?? 500, ct));
        })
            .WithName("GetStreetLightSummary")
            .WithSummary("Street-lamp counts by technology and mount, citywide or within a radius (with lamps per km²).")
            .Produces<StreetLightSummaryDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        return api;
    }

    // ── Public services ──────────────────────────────────────────────────────
    public static RouteGroupBuilder MapPublicServicesEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/services").WithTags(nameof(DataCategory.PublicServices));

        g.MapGet("/cards", async (string? q, int? limit, PublicServicesQueryService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(q, limit ?? 20, ct)))
            .WithName("SearchServiceCards")
            .WithSummary("Search city procedures (BIP service cards).")
            .Produces<IReadOnlyList<ServiceCardDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        g.MapGet("/cards/{id}", async (string id, PublicServicesQueryService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } card ? Results.Ok(card) : Results.NotFound())
            .WithName("GetServiceCard")
            .WithSummary("One city procedure by symbol (e.g. GD-35).")
            .Produces<ServiceCardDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // NFZ waiting lists in Kraków, e.g. /api/services/waiting-lists?benefit=ortoped&urgent=false
        g.MapGet("/waiting-lists", async (string? benefit, bool? urgent, WaitingListQueryService svc, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(benefit) || benefit.Trim().Length < 3)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["benefit"] = ["Give at least 3 letters of the service name, e.g. 'ortoped'."] });
            return Results.Ok(await svc.SearchAsync(benefit, urgent ?? false, ct));
        })
            .WithName("SearchWaitingLists")
            .WithSummary("NFZ waiting lists for a service in Kraków, shortest average wait first.")
            .Produces<IReadOnlyList<WaitingListDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

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
        })
            .WithName("GetOpenDataTables")
            .WithSummary("Tables available from the city Open Data API.")
            .Produces<IReadOnlyList<OpenDataTableDto>>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        g.MapGet("/tables/{key}", async (string key, int? limit, string? q, OpenDataPortalService svc, CancellationToken ct) =>
            await svc.GetRowsAsync(key, limit, q, ct) is { } rows ? Results.Ok(rows) : Results.NotFound())
            .WithName("GetOpenDataRows")
            .WithSummary("Rows of one city Open Data table, as published.")
            .Produces<OpenDataRowsDto>()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    /// <summary>Optional lat/lon pair: both or neither, and valid if given.</summary>
    private static bool TryReadPoint(double? lat, double? lon, out GeoPoint? point, out IResult? error)
    {
        point = null;
        error = null;
        if (lat is null && lon is null) return true;

        var candidate = new GeoPoint(lat ?? double.NaN, lon ?? double.NaN);
        if (lat is null || lon is null || !candidate.IsValid)
        {
            error = Results.ValidationProblem(new Dictionary<string, string[]> { ["lat,lon"] = ["Give both lat and lon as valid coordinates."] });
            return false;
        }

        point = candidate;
        return true;
    }
}
