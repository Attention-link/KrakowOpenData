using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Services;
using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.DataSources;
using KrakowOpenData.Infrastructure.Gios;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.GtfsRealtime;
using KrakowOpenData.Infrastructure.Imgw;
using KrakowOpenData.Infrastructure.Nfz;
using KrakowOpenData.Infrastructure.OpenDataPortal;
using KrakowOpenData.Infrastructure.Options;
using KrakowOpenData.Infrastructure.Repositories;
using KrakowOpenData.Infrastructure.Sample;
using KrakowOpenData.Infrastructure.Seed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure;

/// <summary>
/// Composition root for data access. Call <c>services.AddKrakowOpenData(configuration)</c> from the host.
/// To add a dataset: create a domain record, a data source here, a repository registration, and a
/// service method in Application – nothing else needs to change.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddKrakowOpenData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KrakowDataOptions>(configuration.GetSection(KrakowDataOptions.SectionName));
        var options = configuration.GetSection(KrakowDataOptions.SectionName).Get<KrakowDataOptions>() ?? new KrakowDataOptions();

        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpClient(GtfsDatasetProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KrakowOpenData/0.1 (hackathon prototype)");
        });

        // ── Transit feeds: live or sample ─────────────────────────────────────
        if (options.UseSampleData)
        {
            services.AddSingleton<IGtfsDatasetProvider, SampleGtfsDatasetProvider>();
            services.AddSingleton<IGtfsRealtimeFeedProvider, SampleRealtimeFeedProvider>();
        }
        else
        {
            services.AddSingleton<IGtfsDatasetProvider, GtfsDatasetProvider>();
            services.AddSingleton<IGtfsRealtimeFeedProvider, GtfsRealtimeFeedProvider>();
        }

        services.AddMemoryCache();
        services.AddSingleton<ImgwClient>();
        services.AddSingleton<GiosClient>();

        // ── City Open Data portal tables and NFZ waiting lists: live or sample ──
        if (options.UseSampleData)
        {
            services.AddSingleton<IOpenDataTableReader, SampleOpenDataTableReader>();
            services.AddSingleton<IWaitingListSource, SampleWaitingListSource>();
        }
        else
        {
            services.AddSingleton<IOpenDataTableReader, OpenDataPortalClient>();
            services.AddSingleton<IWaitingListSource, NfzClient>();
        }

        services.AddSingleton<ITransitScheduleRepository, GtfsScheduleRepository>();

        // ── Mobility ──────────────────────────────────────────────────────────
        AddRepository<TransitStop>(services, sp =>
        {
            var gtfs = sp.GetRequiredService<IGtfsDatasetProvider>();
            return new DelegateDataSource<TransitStop>(async ct => (await gtfs.GetAsync(ct)).Stops.Values.ToList());
        });
        AddRepository<TransitRoute>(services, sp =>
        {
            var gtfs = sp.GetRequiredService<IGtfsDatasetProvider>();
            return new DelegateDataSource<TransitRoute>(async ct => (await gtfs.GetAsync(ct)).Routes.Values.ToList());
        });
        AddRepository<VehiclePosition>(services, sp =>
        {
            var rt = sp.GetRequiredService<IGtfsRealtimeFeedProvider>();
            return new DelegateDataSource<VehiclePosition>(async ct => (await rt.GetAsync(ct)).Vehicles);
        });
        AddRepository<TripUpdate>(services, sp =>
        {
            var rt = sp.GetRequiredService<IGtfsRealtimeFeedProvider>();
            return new DelegateDataSource<TripUpdate>(async ct => (await rt.GetAsync(ct)).TripUpdates);
        });
        AddRepository<ServiceAlert>(services, sp =>
        {
            var rt = sp.GetRequiredService<IGtfsRealtimeFeedProvider>();
            return new DelegateDataSource<ServiceAlert>(async ct => (await rt.GetAsync(ct)).Alerts);
        });
        AddRepository<ParkAndRideFacility>(services, _ => SeedData.Source<ParkAndRideFacility>(SeedData.ParkAndRide));

        // ── Environment ───────────────────────────────────────────────────────
        AddRepository<WeatherObservation>(services, sp => options.UseSampleData
            ? SeedData.Source<WeatherObservation>(SeedData.SampleWeather)
            : Cached(sp, new DelegateDataSource<WeatherObservation>(sp.GetRequiredService<ImgwClient>().GetSynopAsync), options.ApiRefreshMinutes));
        AddRepository<AirQualityMeasurement>(services, sp => options.UseSampleData
            ? SeedData.Source<AirQualityMeasurement>(SeedData.SampleAirQuality)
            : Cached(sp, new DelegateDataSource<AirQualityMeasurement>(sp.GetRequiredService<GiosClient>().GetAirQualityAsync), options.AirQualityRefreshMinutes));

        // ── Climate & crisis ──────────────────────────────────────────────────
        AddRepository<HydroObservation>(services, sp =>
        {
            var historical = SeedData.Source<HydroObservation>(SeedData.LocalRiverGauges);
            if (options.UseSampleData) return historical;

            var live = Cached(sp, new DelegateDataSource<HydroObservation>(sp.GetRequiredService<ImgwClient>().GetHydroAsync), options.ApiRefreshMinutes);
            return new CompositeDataSource<HydroObservation>([live, historical], Logger(sp, "RiverGauges"));
        });
        AddRepository<WeatherWarning>(services, sp => options.UseSampleData
            ? SeedData.Source<WeatherWarning>(SeedData.SampleWarnings)
            : Cached(sp, new DelegateDataSource<WeatherWarning>(sp.GetRequiredService<ImgwClient>().GetWarningsAsync), options.ApiRefreshMinutes));

        // ── Urban space & public services ─────────────────────────────────────
        AddRepository<District>(services, _ => SeedData.Source<District>(SeedData.Districts));
        AddRepository<CityServiceCard>(services, _ => SeedData.Source<CityServiceCard>(SeedData.ServiceCards));

        // ── Application use cases ─────────────────────────────────────────────
        services.AddSingleton<CatalogService>();
        services.AddSingleton<TransitQueryService>();
        services.AddSingleton<EnvironmentQueryService>();
        services.AddSingleton<ClimateCrisisQueryService>();
        services.AddSingleton<UrbanSpaceQueryService>();
        services.AddSingleton<PublicServicesQueryService>();
        services.AddSingleton<WaitingListQueryService>();
        services.AddSingleton<OpenDataPortalService>();

        return services;
    }

    private static void AddRepository<T>(IServiceCollection services, Func<IServiceProvider, IDataSource<T>> sourceFactory)
        where T : class, IEntity
    {
        services.AddSingleton<IDataSource<T>>(sourceFactory);
        services.AddSingleton<IReadRepository<T>>(sp => new Repository<T>(sp.GetRequiredService<IDataSource<T>>()));
    }

    private static IDataSource<T> Cached<T>(IServiceProvider sp, IDataSource<T> inner, int minutes) =>
        new CachedDataSource<T>(inner, TimeSpan.FromMinutes(Math.Max(1, minutes)), sp.GetRequiredService<IClock>());

    private static ILogger Logger(IServiceProvider sp, string category) =>
        sp.GetRequiredService<ILoggerFactory>().CreateLogger($"KrakowOpenData.{category}");
}
