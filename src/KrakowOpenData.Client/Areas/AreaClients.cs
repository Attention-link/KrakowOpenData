using KrakowOpenData.Client.Http;
using KrakowOpenData.Contracts;

namespace KrakowOpenData.Client.Areas;

// Each area client only maps method arguments to an endpoint URL; IApiTransport does the HTTP work.

internal sealed class CatalogClient(IApiTransport api) : ICatalogClient
{
    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<CategoryDto>>("api/catalog", ct);

    public Task<DatasetDto?> GetDatasetAsync(string key, CancellationToken ct = default) =>
        api.GetOrNullAsync<DatasetDto>($"api/catalog/{Url.Segment(key)}", ct);
}

internal sealed class MobilityClient(IApiTransport api) : IMobilityClient
{
    public Task<PagedResult<StopDto>> SearchStopsAsync(string? query, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        api.GetAsync<PagedResult<StopDto>>(Url.Build("api/mobility/stops", ("q", query), ("page", page), ("pageSize", pageSize)), ct);

    public Task<IReadOnlyList<StopDto>> GetNearbyStopsAsync(double lat, double lon, int radiusMeters = 500, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<StopDto>>(Url.Build("api/mobility/stops/nearby", ("lat", lat), ("lon", lon), ("radius", radiusMeters)), ct);

    public Task<StopDto?> GetStopAsync(string stopId, CancellationToken ct = default) =>
        api.GetOrNullAsync<StopDto>($"api/mobility/stops/{Url.Segment(stopId)}", ct);

    public Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(string stopId, int limit = 10, int windowMinutes = 60, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<DepartureDto>>(
            Url.Build($"api/mobility/stops/{Url.Segment(stopId)}/departures", ("limit", limit), ("windowMinutes", windowMinutes)), ct);

    public Task<IReadOnlyList<RouteDto>> GetRoutesAsync(string? mode = null, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<RouteDto>>(Url.Build("api/mobility/routes", ("mode", mode)), ct);

    public Task<IReadOnlyList<VehicleDto>> GetVehiclesAsync(string? routeId = null, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<VehicleDto>>(Url.Build("api/mobility/vehicles", ("routeId", routeId)), ct);

    public Task<IReadOnlyList<TripUpdateDto>> GetTripUpdatesAsync(int? minDelaySeconds = null, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<TripUpdateDto>>(Url.Build("api/mobility/trip-updates", ("minDelaySeconds", minDelaySeconds)), ct);

    public Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<AlertDto>>("api/mobility/alerts", ct);

    public Task<IReadOnlyList<ParkAndRideDto>> GetParkAndRideAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<ParkAndRideDto>>("api/mobility/park-and-ride", ct);
}

internal sealed class EnvironmentClient(IApiTransport api) : IEnvironmentClient
{
    public Task<IReadOnlyList<WeatherDto>> GetWeatherAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<WeatherDto>>("api/environment/weather", ct);

    public Task<IReadOnlyList<AirQualityDto>> GetAirQualityAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<AirQualityDto>>("api/environment/air-quality", ct);
}

internal sealed class ClimateCrisisClient(IApiTransport api) : IClimateCrisisClient
{
    public Task<IReadOnlyList<RiverGaugeDto>> GetRiverGaugesAsync(bool elevatedOnly = false, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<RiverGaugeDto>>(Url.Build("api/crisis/river-gauges", ("elevatedOnly", elevatedOnly)), ct);

    public Task<IReadOnlyList<WarningDto>> GetWarningsAsync(string? teryt = null, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<WarningDto>>(Url.Build("api/crisis/warnings", ("teryt", teryt)), ct);
}

internal sealed class UrbanSpaceClient(IApiTransport api) : IUrbanSpaceClient
{
    public Task<IReadOnlyList<DistrictDto>> GetDistrictsAsync(CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<DistrictDto>>("api/urban/districts", ct);

    public Task<DistrictDto?> GetDistrictAsync(string id, CancellationToken ct = default) =>
        api.GetOrNullAsync<DistrictDto>($"api/urban/districts/{Url.Segment(id)}", ct);

    public Task<IReadOnlyList<AmenityDto>> GetAmenitiesAsync(string? kind = null, double? lat = null, double? lon = null,
        int radiusMeters = 1000, int limit = 500, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<AmenityDto>>(Url.Build("api/urban/amenities",
            ("kind", kind), ("lat", lat), ("lon", lon), ("radius", radiusMeters), ("limit", limit)), ct);

    public Task<IReadOnlyList<StreetLightDto>> GetStreetLightsAsync(double? lat = null, double? lon = null, int radiusMeters = 500,
        string? technology = null, int? limit = null, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<StreetLightDto>>(Url.Build("api/urban/street-lights",
            ("lat", lat), ("lon", lon), ("radius", radiusMeters), ("technology", technology), ("limit", limit)), ct);

    public Task<StreetLightSummaryDto> GetStreetLightSummaryAsync(double? lat = null, double? lon = null, int radiusMeters = 500,
        CancellationToken ct = default) =>
        api.GetAsync<StreetLightSummaryDto>(Url.Build("api/urban/street-lights/summary",
            ("lat", lat), ("lon", lon), ("radius", radiusMeters)), ct);
}

internal sealed class PublicServicesClient(IApiTransport api) : IPublicServicesClient
{
    public Task<IReadOnlyList<ServiceCardDto>> SearchServiceCardsAsync(string? query, int limit = 20, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<ServiceCardDto>>(Url.Build("api/services/cards", ("q", query), ("limit", limit)), ct);

    public Task<ServiceCardDto?> GetServiceCardAsync(string id, CancellationToken ct = default) =>
        api.GetOrNullAsync<ServiceCardDto>($"api/services/cards/{Url.Segment(id)}", ct);

    public Task<IReadOnlyList<WaitingListDto>> SearchWaitingListsAsync(string benefit, bool urgent = false, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<WaitingListDto>>(Url.Build("api/services/waiting-lists", ("benefit", benefit), ("urgent", urgent)), ct);
}

internal sealed class OpenDataClient(IApiTransport api) : IOpenDataClient
{
    public Task<IReadOnlyList<OpenDataTableDto>> GetTablesAsync(string? category = null, CancellationToken ct = default) =>
        api.GetAsync<IReadOnlyList<OpenDataTableDto>>(Url.Build("api/open-data/tables", ("category", category)), ct);

    public Task<OpenDataRowsDto?> GetRowsAsync(string key, int limit = 500, string? query = null, CancellationToken ct = default) =>
        api.GetOrNullAsync<OpenDataRowsDto>(Url.Build($"api/open-data/tables/{Url.Segment(key)}", ("limit", limit), ("q", query)), ct);
}
