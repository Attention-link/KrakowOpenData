using System.Net;
using System.Net.Http.Json;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Contracts;

namespace KrakowOpenData.Web.Services;

/// <summary>Typed client for KrakowOpenData.Api. One method per endpoint; pages only talk to this.</summary>
public sealed class KrakowApiClient(HttpClient http)
{
    // Catalog
    public Task<List<CategoryDto>?> GetCatalogAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<CategoryDto>>("api/catalog", ct);

    // Mobility
    public Task<PagedResult<StopDto>?> SearchStopsAsync(string? query, int page, int pageSize, CancellationToken ct = default) =>
        http.GetFromJsonAsync<PagedResult<StopDto>>(
            $"api/mobility/stops?q={Uri.EscapeDataString(query ?? string.Empty)}&page={page}&pageSize={pageSize}", ct);

    public Task<List<StopDto>?> GetNearbyStopsAsync(double lat, double lon, int radius, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<StopDto>>(
            FormattableString.Invariant($"api/mobility/stops/nearby?lat={lat}&lon={lon}&radius={radius}"), ct);

    public Task<StopDto?> GetStopAsync(string stopId, CancellationToken ct = default) =>
        GetOrNullAsync<StopDto>($"api/mobility/stops/{Uri.EscapeDataString(stopId)}", ct);

    public Task<List<DepartureDto>?> GetDeparturesAsync(string stopId, int limit, int windowMinutes, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<DepartureDto>>(
            $"api/mobility/stops/{Uri.EscapeDataString(stopId)}/departures?limit={limit}&windowMinutes={windowMinutes}", ct);

    public Task<List<RouteDto>?> GetRoutesAsync(string? mode, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<RouteDto>>($"api/mobility/routes?mode={Uri.EscapeDataString(mode ?? string.Empty)}", ct);

    public Task<List<VehicleDto>?> GetVehiclesAsync(string? routeId, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<VehicleDto>>($"api/mobility/vehicles?routeId={Uri.EscapeDataString(routeId ?? string.Empty)}", ct);

    public Task<List<AlertDto>?> GetAlertsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<AlertDto>>("api/mobility/alerts", ct);

    public Task<List<ParkAndRideDto>?> GetParkAndRideAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<ParkAndRideDto>>("api/mobility/park-and-ride", ct);

    // Environment
    public Task<List<WeatherDto>?> GetWeatherAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<WeatherDto>>("api/environment/weather", ct);

    public Task<List<AirQualityDto>?> GetAirQualityAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<AirQualityDto>>("api/environment/air-quality", ct);

    // Climate & crisis
    public Task<List<RiverGaugeDto>?> GetRiverGaugesAsync(bool elevatedOnly, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<RiverGaugeDto>>($"api/crisis/river-gauges?elevatedOnly={(elevatedOnly ? "true" : "false")}", ct);

    public Task<List<WarningDto>?> GetWarningsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<WarningDto>>("api/crisis/warnings", ct);

    // Urban space
    public Task<List<DistrictDto>?> GetDistrictsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<DistrictDto>>("api/urban/districts", ct);

    // Public services
    public Task<List<ServiceCardDto>?> SearchServiceCardsAsync(string? query, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<ServiceCardDto>>($"api/services/cards?q={Uri.EscapeDataString(query ?? string.Empty)}", ct);

    public Task<List<WaitingListDto>?> SearchWaitingListsAsync(string benefit, bool urgent, CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<WaitingListDto>>(
            $"api/services/waiting-lists?benefit={Uri.EscapeDataString(benefit)}&urgent={(urgent ? "true" : "false")}", ct);

    // City Open Data portal
    public Task<List<OpenDataTableDto>?> GetOpenDataTablesAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<List<OpenDataTableDto>>("api/open-data/tables", ct);

    public Task<OpenDataRowsDto?> GetOpenDataRowsAsync(string key, int limit, CancellationToken ct = default) =>
        GetOrNullAsync<OpenDataRowsDto>($"api/open-data/tables/{Uri.EscapeDataString(key)}?limit={limit}", ct);

    private async Task<T?> GetOrNullAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }
}
