using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Safety;
using KrakowOpenData.Infrastructure.Safety;
using KrakowOpenData.Application.Services;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.DataSources;
using KrakowOpenData.Infrastructure.Geocoding;
using KrakowOpenData.Infrastructure.Gios;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.GtfsRealtime;
using KrakowOpenData.Infrastructure.Imgw;
using KrakowOpenData.Infrastructure.Nfz;
using KrakowOpenData.Infrastructure.OpenDataPortal;
using KrakowOpenData.Infrastructure.OpenStreetMap;
using KrakowOpenData.Infrastructure.Options;
using KrakowOpenData.Infrastructure.Repositories;
using KrakowOpenData.Infrastructure.Telegram;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KrakowOpenData.Infrastructure;

/// <summary>
/// Composition root for data access. Call <c>services.AddKrakowOpenData(configuration)</c> from the host.
/// Every dataset is read live from its public source and cached. To add a dataset: create a domain
/// record, a data source here, and a service method in Application – nothing else needs to change.
/// Tests replace the <see cref="IDataSource{T}"/> and provider registrations with in-memory fakes.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddKrakowOpenData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KrakowDataOptions>(configuration.GetSection(KrakowDataOptions.SectionName));
        var options = configuration.GetSection(KrakowDataOptions.SectionName).Get<KrakowDataOptions>() ?? new KrakowDataOptions();

        services.AddSingleton<IClock, SystemClock>();
        services.AddMemoryCache();
        services.AddHttpClient(GtfsDatasetProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowOpenData/0.1 (hackathon prototype)");
        });

        // ── Upstream clients (one per public source) ──────────────────────────
        services.AddSingleton<IGtfsDatasetProvider, GtfsDatasetProvider>();
        services.AddSingleton<IGtfsRealtimeFeedProvider, GtfsRealtimeFeedProvider>();
        services.AddSingleton<ImgwClient>();
        services.AddSingleton<GiosClient>();
        services.AddHttpClient(OverpassQueryRunner.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Max(30, options.OverpassTimeoutSeconds));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowOpenData/0.1 (hackathon prototype)");
        });
        services.AddSingleton<ISnapshotStore, FileSnapshotStore>();
        services.AddSingleton<OverpassQueryRunner>();
        services.AddSingleton<OverpassClient>();
        services.AddSingleton<StreetLightsClient>();
        services.AddSingleton<SafetyPlacesClient>();
        services.AddHostedService<OsmPreloadService>();
        services.AddHttpClient(PhotonGeocoder.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowOpenData/0.1 (hackathon prototype)");
        });
        services.AddSingleton<IGeocoder, PhotonGeocoder>();
        services.Configure<RoutingOptions>(configuration.GetSection(RoutingOptions.SectionName));
        services.AddHttpClient(OsrmWalkingRouter.HttpClientName, (sp, client) =>
        {
            var routing = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoutingOptions>>().Value;
            client.BaseAddress = new Uri(routing.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(12);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowOpenData/0.1 (hackathon prototype)");
        });
        services.AddSingleton<IWalkingRouter, OsrmWalkingRouter>();
        services.AddSingleton<IOpenDataTableReader, OpenDataPortalClient>();
        services.AddSingleton<IWaitingListSource, NfzClient>();
        services.AddSingleton<ITransitScheduleRepository, GtfsScheduleRepository>();

        // ── Mobility: ZTP GTFS / GTFS-RT, OpenStreetMap ──────────────────────
        AddRepository<TransitStop>(services, sp => From<TransitStop>(async ct => (await Gtfs(sp).GetAsync(ct)).Stops.Values.ToList()));
        AddRepository<TransitRoute>(services, sp => From<TransitRoute>(async ct => (await Gtfs(sp).GetAsync(ct)).Routes.Values.ToList()));
        AddRepository<VehiclePosition>(services, sp => From<VehiclePosition>(async ct => (await Realtime(sp).GetAsync(ct)).Vehicles));
        AddRepository<TripUpdate>(services, sp => From<TripUpdate>(async ct => (await Realtime(sp).GetAsync(ct)).TripUpdates));
        AddRepository<ServiceAlert>(services, sp => From<ServiceAlert>(async ct => (await Realtime(sp).GetAsync(ct)).Alerts));
        AddRepository<ParkAndRideFacility>(services, sp => From<ParkAndRideFacility>(async ct => (await Osm(sp).GetAsync(ct)).ParkAndRide));

        // ── Environment: IMGW, GIOŚ ───────────────────────────────────────────
        AddRepository<WeatherObservation>(services, sp =>
            Cached(sp, From<WeatherObservation>(sp.GetRequiredService<ImgwClient>().GetSynopAsync), options.ApiRefreshMinutes));
        AddRepository<AirQualityMeasurement>(services, sp =>
            Cached(sp, From<AirQualityMeasurement>(sp.GetRequiredService<GiosClient>().GetAirQualityAsync), options.AirQualityRefreshMinutes));

        // ── Climate & crisis: IMGW ────────────────────────────────────────────
        AddRepository<HydroObservation>(services, sp =>
            Cached(sp, From<HydroObservation>(sp.GetRequiredService<ImgwClient>().GetHydroAsync), options.ApiRefreshMinutes));
        AddRepository<WeatherWarning>(services, sp =>
            Cached(sp, From<WeatherWarning>(sp.GetRequiredService<ImgwClient>().GetWarningsAsync), options.ApiRefreshMinutes));

        // ── Urban space: city Open Data API, OpenStreetMap ────────────────────
        AddRepository<District>(services, sp => From<District>(async ct => CityTableMapper.Districts(
            await Tables(sp).ReadAsync(OpenDataTables.ResidentsByDistrict, 1000, ct), "31 Dec 2025")));
        AddRepository<Amenity>(services, sp => From<Amenity>(async ct => (await Osm(sp).GetAsync(ct)).Amenities));
        AddRepository<StreetLight>(services, sp => From<StreetLight>(sp.GetRequiredService<StreetLightsClient>().GetAsync));
        AddRepository<SafetyPlace>(services, sp => From<SafetyPlace>(sp.GetRequiredService<SafetyPlacesClient>().GetAsync));

        // ── Public services: city Open Data API ───────────────────────────────
        AddRepository<CityServiceCard>(services, sp => From<CityServiceCard>(async ct => CityTableMapper.ServiceCards(
            await Tables(sp).ReadAsync(OpenDataTables.CityProcedures, 5000, ct))));

        // ── Application use cases ─────────────────────────────────────────────
        services.AddSingleton<CatalogService>();
        services.AddSingleton<TransitQueryService>();
        services.AddSingleton<EnvironmentQueryService>();
        services.AddSingleton<ClimateCrisisQueryService>();
        services.AddSingleton<UrbanSpaceQueryService>();
        services.AddSingleton<StreetLightsQueryService>();
        services.AddSingleton<PublicServicesQueryService>();
        services.AddSingleton<WaitingListQueryService>();
        services.AddSingleton<OpenDataPortalService>();

        // ── Safety concerns: heat + night-safety scores, citizen reports, planner tools ──
        services.Configure<SafetyOptions>(configuration.GetSection(SafetyOptions.SectionName));
        services.AddSingleton<ISafetyStore, JsonFileSafetyStore>();
        services.AddSingleton<IAgencyGateway, SimulatedAgencyGateway>();
        services.AddSingleton<PresenceTracker>();
        services.AddSingleton<SafetyModelProvider>();
        services.AddSingleton<ConditionsService>();
        services.AddSingleton<ScoreService>();
        services.AddSingleton<RouteService>();
        services.AddSingleton<MethodService>();
        services.AddSingleton<WeightService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<AlertService>();
        services.AddSingleton<AgencyService>();
        services.AddSingleton<PlannerService>();
        services.AddSingleton<DemoDataService>();
        services.AddSafetyNotifications(configuration); // events, Workers AI triage / voice, Telegram bot (all off without config)

        return services;
    }

    private static void AddRepository<T>(IServiceCollection services, Func<IServiceProvider, IDataSource<T>> sourceFactory)
        where T : class, IEntity
    {
        services.AddSingleton<IDataSource<T>>(sourceFactory);
        services.AddSingleton<IReadRepository<T>>(sp => new Repository<T>(sp.GetRequiredService<IDataSource<T>>()));
    }

    private static IDataSource<T> From<T>(Func<CancellationToken, Task<IReadOnlyList<T>>> load) => new DelegateDataSource<T>(load);

    private static IDataSource<T> Cached<T>(IServiceProvider sp, IDataSource<T> inner, int minutes) =>
        new CachedDataSource<T>(inner, TimeSpan.FromMinutes(Math.Max(1, minutes)), sp.GetRequiredService<IClock>());

    private static IGtfsDatasetProvider Gtfs(IServiceProvider sp) => sp.GetRequiredService<IGtfsDatasetProvider>();

    private static IGtfsRealtimeFeedProvider Realtime(IServiceProvider sp) => sp.GetRequiredService<IGtfsRealtimeFeedProvider>();

    private static OverpassClient Osm(IServiceProvider sp) => sp.GetRequiredService<OverpassClient>();

    private static IOpenDataTableReader Tables(IServiceProvider sp) => sp.GetRequiredService<IOpenDataTableReader>();
}
