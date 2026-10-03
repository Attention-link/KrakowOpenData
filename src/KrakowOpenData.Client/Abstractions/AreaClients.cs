using KrakowOpenData.Contracts;

namespace KrakowOpenData.Client;

// One small interface per data area (interface segregation): consumers depend only on what they use,
// and each is easy to fake in their tests. Methods returning a single item return null for 404.
// Every method throws KrakowApiException for other API errors.

/// <summary>Which datasets exist, grouped by category.</summary>
public interface ICatalogClient
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<DatasetDto?> GetDatasetAsync(string key, CancellationToken ct = default);
}

/// <summary>Public transport (ZTP GTFS / GTFS-Realtime) and P+R car parks.</summary>
public interface IMobilityClient
{
    Task<PagedResult<StopDto>> SearchStopsAsync(string? query, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<StopDto>> GetNearbyStopsAsync(double lat, double lon, int radiusMeters = 500, CancellationToken ct = default);
    Task<StopDto?> GetStopAsync(string stopId, CancellationToken ct = default);
    Task<IReadOnlyList<DepartureDto>> GetDeparturesAsync(string stopId, int limit = 10, int windowMinutes = 60, CancellationToken ct = default);

    /// <param name="mode">Tram, Bus, Rail, Metro or Other; null for all.</param>
    Task<IReadOnlyList<RouteDto>> GetRoutesAsync(string? mode = null, CancellationToken ct = default);
    Task<IReadOnlyList<VehicleDto>> GetVehiclesAsync(string? routeId = null, CancellationToken ct = default);
    Task<IReadOnlyList<TripUpdateDto>> GetTripUpdatesAsync(int? minDelaySeconds = null, CancellationToken ct = default);
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ParkAndRideDto>> GetParkAndRideAsync(CancellationToken ct = default);
}

/// <summary>Weather (IMGW) and air quality (GIOŚ).</summary>
public interface IEnvironmentClient
{
    Task<IReadOnlyList<WeatherDto>> GetWeatherAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AirQualityDto>> GetAirQualityAsync(CancellationToken ct = default);
}

/// <summary>River levels and weather/hydrological warnings (IMGW).</summary>
public interface IClimateCrisisClient
{
    Task<IReadOnlyList<RiverGaugeDto>> GetRiverGaugesAsync(bool elevatedOnly = false, CancellationToken ct = default);

    /// <param name="teryt">Area code; the server defaults to Kraków (1261).</param>
    Task<IReadOnlyList<WarningDto>> GetWarningsAsync(string? teryt = null, CancellationToken ct = default);
}

/// <summary>Districts (city Open Data API) and public amenities (OpenStreetMap).</summary>
public interface IUrbanSpaceClient
{
    Task<IReadOnlyList<DistrictDto>> GetDistrictsAsync(CancellationToken ct = default);

    /// <param name="id">Roman numeral, e.g. "XIV".</param>
    Task<DistrictDto?> GetDistrictAsync(string id, CancellationToken ct = default);

    /// <param name="kind">Defibrillator, DrinkingWater, Toilets, EvCharger or BikeParking; null for all.</param>
    /// <param name="lat">With <paramref name="lon"/>: only amenities near this point, nearest first.</param>
    Task<IReadOnlyList<AmenityDto>> GetAmenitiesAsync(string? kind = null, double? lat = null, double? lon = null,
        int radiusMeters = 1000, int limit = 500, CancellationToken ct = default);

    /// <summary>Street lamps (OpenStreetMap). With lat/lon: within the radius, nearest first; otherwise citywide.</summary>
    /// <param name="technology">Led, Sodium, Mercury, MetalHalide, Other or Unknown; null for all.</param>
    Task<IReadOnlyList<StreetLightDto>> GetStreetLightsAsync(double? lat = null, double? lon = null, int radiusMeters = 500,
        string? technology = null, int? limit = null, CancellationToken ct = default);

    /// <summary>Lamp counts by technology and mount, citywide or within a radius (with lamps per km²).</summary>
    Task<StreetLightSummaryDto> GetStreetLightSummaryAsync(double? lat = null, double? lon = null, int radiusMeters = 500,
        CancellationToken ct = default);
}

/// <summary>City procedures (BIP service cards) and NFZ waiting lists.</summary>
public interface IPublicServicesClient
{
    Task<IReadOnlyList<ServiceCardDto>> SearchServiceCardsAsync(string? query, int limit = 20, CancellationToken ct = default);
    Task<ServiceCardDto?> GetServiceCardAsync(string id, CancellationToken ct = default);

    /// <param name="benefit">At least 3 letters of the service name in Polish, e.g. "ortoped".</param>
    Task<IReadOnlyList<WaitingListDto>> SearchWaitingListsAsync(string benefit, bool urgent = false, CancellationToken ct = default);
}

/// <summary>Generic tables from the City of Kraków Open Data API.</summary>
public interface IOpenDataClient
{
    /// <param name="category">A category name such as "Society"; null for all.</param>
    Task<IReadOnlyList<OpenDataTableDto>> GetTablesAsync(string? category = null, CancellationToken ct = default);

    /// <param name="query">Optional text filter applied to every column (diacritics ignored).</param>
    Task<OpenDataRowsDto?> GetRowsAsync(string key, int limit = 500, string? query = null, CancellationToken ct = default);
}
