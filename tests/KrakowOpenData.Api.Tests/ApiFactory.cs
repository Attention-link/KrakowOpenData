using KrakowOpenData.Api.Tests.Fakes;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Infrastructure.Gtfs;
using KrakowOpenData.Infrastructure.GtfsRealtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace KrakowOpenData.Api.Tests;

/// <summary>
/// Boots the real API in-process with every live source replaced by in-memory fakes, so tests
/// need no network. Later registrations win, so these override AddKrakowOpenData's.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("KrakowData:PreloadOnStartup", "false"); // no OpenStreetMap downloads in tests
        builder.UseSetting("Safety:Persist", "false");               // reports and alerts stay in memory
        builder.UseSetting("Safety:WriteRequestsPerMinute", "0");    // the shared test client would trip the per-IP limit; its own test turns it on
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IGtfsDatasetProvider, FakeGtfsDatasetProvider>();
            services.AddSingleton<IGtfsRealtimeFeedProvider, FakeRealtimeFeedProvider>();
            services.AddSingleton<IOpenDataTableReader, FakeOpenDataTableReader>();
            services.AddSingleton<IWaitingListSource, FakeWaitingListSource>();
            services.AddSingleton<IGeocoder, FakeGeocoder>();
            services.AddSingleton<IWalkingRouter, FakeWalkingRouter>();
            // Telegram and AI are not configured here (no token, no worker), so nothing leaves the process; events are only recorded.
            services.AddSingleton<CapturingSafetyEventSink>();
            services.AddSingleton<KrakowOpenData.Application.Safety.ISafetyEventSink>(sp => sp.GetRequiredService<CapturingSafetyEventSink>());

            services.AddSingleton(FakeData.Of(FakeData.Weather));
            services.AddSingleton(FakeData.Of(FakeData.AirQuality));
            services.AddSingleton(FakeData.Of(FakeData.RiverGauges));
            services.AddSingleton(FakeData.Of(FakeData.Warnings));
            services.AddSingleton(FakeData.Of(FakeData.ParkAndRide));
            services.AddSingleton(FakeData.Of(FakeData.Districts));
            services.AddSingleton(FakeData.Of(FakeData.ServiceCards));
            services.AddSingleton(FakeData.Of(FakeData.Amenities));
            services.AddSingleton(FakeData.Of(FakeData.StreetLights));
            services.AddSingleton(FakeData.Of(FakeData.SafetyPlaces));
        });
    }
}
