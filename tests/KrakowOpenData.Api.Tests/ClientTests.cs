using System.Net;
using KrakowOpenData.Client;
using Microsoft.Extensions.DependencyInjection;

namespace KrakowOpenData.Api.Tests;

/// <summary>Runs KrakowOpenData.Client against the in-process API (fake data, no network).</summary>
public class ClientTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly IKrakowOpenDataClient _api = new KrakowOpenDataClient(factory.CreateClient());

    [Fact]
    public async Task Client_reads_every_area()
    {
        Assert.NotEmpty(await _api.Catalog.GetCategoriesAsync());
        Assert.NotEmpty((await _api.Mobility.SearchStopsAsync("dworzec")).Items);
        Assert.NotEmpty(await _api.Mobility.GetParkAndRideAsync());
        Assert.NotEmpty(await _api.Environment.GetAirQualityAsync());
        Assert.NotEmpty(await _api.Crisis.GetRiverGaugesAsync());
        Assert.Equal(18, (await _api.Urban.GetDistrictsAsync()).Count);
        Assert.Single(await _api.Urban.GetAmenitiesAsync("Defibrillator", 50.0617, 19.9373, 500));
        Assert.Single(await _api.Urban.GetStreetLightsAsync(technology: "Led"));
        Assert.Equal(2, (await _api.Urban.GetStreetLightSummaryAsync(50.0617, 19.9373, 200)).Total);
        Assert.NotEmpty(await _api.Services.SearchWaitingListsAsync("ortoped"));
        Assert.Equal(2, (await _api.OpenData.GetRowsAsync("unemployed", 2))!.Rows.Count);
    }

    [Fact]
    public async Task Client_returns_null_for_missing_items()
    {
        Assert.Null(await _api.Mobility.GetStopAsync("S:999"));
        Assert.Null(await _api.Urban.GetDistrictAsync("XX"));
        Assert.Null(await _api.OpenData.GetRowsAsync("nope"));
    }

    [Fact]
    public async Task Client_turns_validation_errors_into_exceptions()
    {
        var ex = await Assert.ThrowsAsync<KrakowApiException>(() => _api.Services.SearchWaitingListsAsync("x"));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.False(ex.IsUpstreamUnavailable);
        Assert.Contains("benefit", ex.Message);
    }

    [Fact]
    public void Dependency_injection_registers_the_facade_and_every_area()
    {
        var services = new ServiceCollection();
        services.AddKrakowOpenDataClient("http://localhost:5080");
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IKrakowOpenDataClient>().Mobility);
        Assert.NotNull(provider.GetRequiredService<IEnvironmentClient>());
        Assert.NotNull(provider.GetRequiredService<IOpenDataClient>());
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.Equal(new Uri("http://localhost:5080/"), http.BaseAddress);
    }
}
