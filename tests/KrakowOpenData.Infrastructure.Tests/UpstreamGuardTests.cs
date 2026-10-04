using System.Net;
using System.Text;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.Geocoding;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace KrakowOpenData.Infrastructure.Tests;

/// <summary>
/// A scripted public service: counts requests, answers with <see cref="Respond"/>, and can hold answers back to measure how
/// many run at once. Its clients get a base address, like the named "routing" client does in DependencyInjection.
/// </summary>
public sealed class ScriptedUpstream : HttpMessageHandler, IHttpClientFactory
{
    private int _inFlight;

    public List<string> Requests { get; } = [];
    public int MaxInFlight { get; private set; }
    public TaskCompletionSource? Hold { get; set; }

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => Ok(OsrmJson);

    public const string OsrmJson = """{"code":"Ok","routes":[{"distance":120.5,"geometry":{"coordinates":[[19.93,50.06],[19.94,50.061]]}}]}""";
    public const string PhotonJson = """{"features":[{"geometry":{"coordinates":[19.9371,50.0615]},"properties":{"name":"Rynek Główny","district":"Stare Miasto","type":"street"}}]}""";

    public static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("""{"code":"Error"}""") };
        if (retryAfterSeconds is { } s) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(s));
        return response;
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false) { BaseAddress = new Uri("https://upstream.test/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests) Requests.Add(request.RequestUri!.AbsoluteUri);
        var now = Interlocked.Increment(ref _inFlight);
        lock (Requests) MaxInFlight = Math.Max(MaxInFlight, now);
        try
        {
            if (Hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
            return Respond(request);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

public class BoundedTtlCacheTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void An_entry_is_served_until_it_expires()
    {
        var cache = new BoundedTtlCache<string>(10, TimeSpan.FromMinutes(10), _clock);
        cache.Set("a", "1");
        _clock.Advance(TimeSpan.FromMinutes(9));
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal("1", value);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void When_full_the_oldest_entry_goes()
    {
        var cache = new BoundedTtlCache<int>(3, TimeSpan.FromMinutes(10), _clock);
        for (var i = 0; i < 5; i++) cache.Set($"k{i}", i);
        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGet("k0", out _));
        Assert.False(cache.TryGet("k1", out _));
        Assert.True(cache.TryGet("k4", out var newest) && newest == 4);

        cache.Set("k2", 22);   // a rewrite counts as new, so k3 is now the oldest
        cache.Set("k5", 5);
        Assert.False(cache.TryGet("k3", out _));
        Assert.True(cache.TryGet("k2", out var rewritten) && rewritten == 22);
    }

    [Fact]
    public void A_cached_null_is_a_hit()
    {
        var cache = new BoundedTtlCache<string?>(3, TimeSpan.FromMinutes(10), _clock);
        cache.Set("nothing here", null);
        Assert.True(cache.TryGet("nothing here", out var value));
        Assert.Null(value);
    }
}

public class OsrmRouterFairUseTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedUpstream _upstream = new();

    private static readonly GeoPoint A = new(50.0610, 19.9370);
    private static readonly GeoPoint B = new(50.0640, 19.9380);

    private OsrmWalkingRouter Router() => new(_upstream, _clock, NullLogger<OsrmWalkingRouter>.Instance);

    [Fact]
    public async Task The_same_trip_is_routed_once_and_coordinates_are_rounded_to_about_a_metre()
    {
        var router = Router();
        var first = await router.RouteAsync([A, B], true);
        var again = await router.RouteAsync([new GeoPoint(50.061000004, 19.937000003), B], true);   // a GPS jitter of millimetres

        Assert.Single(_upstream.Requests);
        Assert.Same(first, again);
        Assert.Contains("19.93700,50.06100;19.93800,50.06400", _upstream.Requests[0]);
        await router.RouteAsync([A, B], false);   // without alternatives it is another question
        Assert.Equal(2, _upstream.Requests.Count);
    }

    [Fact]
    public async Task A_cached_route_expires_after_its_time()
    {
        var router = Router();
        await router.RouteAsync([A, B], false);
        _clock.Advance(OsrmWalkingRouter.CacheTime + TimeSpan.FromSeconds(1));
        await router.RouteAsync([A, B], false);
        Assert.Equal(2, _upstream.Requests.Count);
    }

    [Fact]
    public async Task A_429_pauses_every_call_for_the_retry_after_time_without_retrying()
    {
        var router = Router();
        _upstream.Respond = _ => ScriptedUpstream.Status(HttpStatusCode.TooManyRequests, 20);
        var busy = await Assert.ThrowsAsync<UpstreamBusyException>(() => router.RouteAsync([A, B], true));
        Assert.Equal(20, busy.RetryAfter.TotalSeconds, 0);
        Assert.Single(_upstream.Requests);

        _upstream.Respond = _ => ScriptedUpstream.Ok(ScriptedUpstream.OsrmJson);
        _clock.Advance(TimeSpan.FromSeconds(15));
        await Assert.ThrowsAsync<UpstreamBusyException>(() => router.RouteAsync([A, new GeoPoint(50.07, 19.94)], false));
        Assert.Single(_upstream.Requests);   // skipped: the server is not asked again while paused

        _clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Single(await router.RouteAsync([A, B], true));
        Assert.Equal(2, _upstream.Requests.Count);
    }

    [Fact]
    public async Task A_429_without_retry_after_pauses_for_the_default_and_a_huge_one_is_capped()
    {
        var router = Router();
        _upstream.Respond = _ => ScriptedUpstream.Status(HttpStatusCode.TooManyRequests);
        var busy = await Assert.ThrowsAsync<UpstreamBusyException>(() => router.RouteAsync([A, B], true));
        Assert.Equal(UpstreamGuard.RateLimitedPause, busy.RetryAfter);

        var other = new OsrmWalkingRouter(_upstream, _clock, NullLogger<OsrmWalkingRouter>.Instance);
        _upstream.Respond = _ => ScriptedUpstream.Status(HttpStatusCode.TooManyRequests, 86_400);
        busy = await Assert.ThrowsAsync<UpstreamBusyException>(() => other.RouteAsync([A, B], true));
        Assert.Equal(UpstreamGuard.MaxPause, busy.RetryAfter);
    }

    [Fact]
    public async Task Repeated_server_errors_or_timeouts_pause_calls_briefly()
    {
        var router = Router();
        var calls = 0;
        _upstream.Respond = _ => ++calls == 2 ? throw new TaskCanceledException("timeout") : ScriptedUpstream.Status(HttpStatusCode.BadGateway);

        for (var i = 0; i < UpstreamGuard.FailuresToOpen; i++)
            await Assert.ThrowsAnyAsync<Exception>(() => router.RouteAsync([A, new GeoPoint(50.07 + i * 0.001, 19.94)], false));
        Assert.Equal(UpstreamGuard.FailuresToOpen, _upstream.Requests.Count);

        await Assert.ThrowsAsync<UpstreamBusyException>(() => router.RouteAsync([A, B], false));
        Assert.Equal(UpstreamGuard.FailuresToOpen, _upstream.Requests.Count);

        _upstream.Respond = _ => ScriptedUpstream.Ok(ScriptedUpstream.OsrmJson);
        _clock.Advance(UpstreamGuard.FailurePause + TimeSpan.FromSeconds(1));
        Assert.Single(await router.RouteAsync([A, B], false));
    }

    [Fact]
    public async Task A_503_with_retry_after_pauses_at_once()
    {
        var router = Router();
        _upstream.Respond = _ => ScriptedUpstream.Status(HttpStatusCode.ServiceUnavailable, 10);
        await Assert.ThrowsAsync<HttpRequestException>(() => router.RouteAsync([A, B], false));
        var busy = await Assert.ThrowsAsync<UpstreamBusyException>(() => router.RouteAsync([A, B], false));
        Assert.Equal(10, busy.RetryAfter.TotalSeconds, 0);
        Assert.Single(_upstream.Requests);
    }

    [Fact]
    public async Task No_route_is_an_error_for_that_trip_only_and_never_pauses_the_router()
    {
        var router = Router();
        _upstream.Respond = _ => ScriptedUpstream.Status(HttpStatusCode.BadRequest);
        for (var i = 0; i < UpstreamGuard.FailuresToOpen + 1; i++)
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => router.RouteAsync([A, new GeoPoint(50.07 + i * 0.001, 19.94)], false));
            Assert.IsNotType<UpstreamBusyException>(ex);
        }

        Assert.Equal(UpstreamGuard.FailuresToOpen + 1, _upstream.Requests.Count);
    }

    [Fact]
    public async Task At_most_four_requests_run_at_once_and_the_rest_wait_their_turn()
    {
        var router = Router();
        _upstream.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 10).Select(i => router.RouteAsync([A, new GeoPoint(50.07 + i * 0.001, 19.94)], false)).ToList();

        for (var i = 0; i < 100 && _upstream.Requests.Count < OsrmWalkingRouter.MaxConcurrent; i++) await Task.Delay(10);
        await Task.Delay(50);
        Assert.Equal(OsrmWalkingRouter.MaxConcurrent, _upstream.Requests.Count);

        _upstream.Hold.SetResult();
        await Task.WhenAll(calls);
        Assert.Equal(10, _upstream.Requests.Count);
        Assert.Equal(OsrmWalkingRouter.MaxConcurrent, _upstream.MaxInFlight);
    }
}

public class PhotonFairUseTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedUpstream _upstream = new() { Respond = _ => ScriptedUpstream.Ok(ScriptedUpstream.PhotonJson) };

    private PhotonGeocoder Geocoder() => new(_upstream, _clock, Microsoft.Extensions.Options.Options.Create(new KrakowDataOptions()),
        NullLogger<PhotonGeocoder>.Instance);

    [Fact]
    public async Task The_same_text_in_any_case_or_spacing_is_one_call_for_an_hour()
    {
        var geocoder = Geocoder();
        var first = await geocoder.SearchAsync("Rynek Główny", null, 6);
        await geocoder.SearchAsync("  rynek   GŁÓWNY ", null, 6);
        Assert.Single(first);
        Assert.Single(_upstream.Requests);
        Assert.Contains("q=Rynek%20G%C5%82%C3%B3wny&", _upstream.Requests[0]);

        _clock.Advance(TimeSpan.FromMinutes(61));
        await geocoder.SearchAsync("rynek główny", null, 6);
        Assert.Equal(2, _upstream.Requests.Count);
    }

    [Fact]
    public async Task Reverse_lookups_are_cached_including_places_with_no_address()
    {
        var geocoder = Geocoder();
        _upstream.Respond = _ => ScriptedUpstream.Ok("""{"features":[]}""");
        Assert.Null(await geocoder.ReverseAsync(new GeoPoint(50.06151, 19.93712)));
        Assert.Null(await geocoder.ReverseAsync(new GeoPoint(50.06149, 19.93708)));   // same 4-decimal square
        Assert.Single(_upstream.Requests);
    }

    [Fact]
    public async Task A_429_from_photon_pauses_searches_and_says_when_to_retry()
    {
        var geocoder = Geocoder();
        _upstream.Respond = _ => ScriptedUpstream.Status(HttpStatusCode.TooManyRequests, 30);
        await Assert.ThrowsAsync<UpstreamBusyException>(() => geocoder.SearchAsync("wawel", null, 6));
        var busy = await Assert.ThrowsAsync<UpstreamBusyException>(() => geocoder.SearchAsync("kazimierz", null, 6));
        Assert.Contains("Try again", busy.Message);
        Assert.Single(_upstream.Requests);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
    }

    [Theory]
    [InlineData("  Rynek \t Główny ", "Rynek Główny")]
    [InlineData("wawel", "wawel")]
    public void Typed_text_is_normalised(string typed, string expected) => Assert.Equal(expected, PhotonGeocoder.Normalise(typed));
}

public class UpstreamUserAgentTests
{
    [Fact]
    public void Every_public_source_client_identifies_the_app()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddKrakowOpenData(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        using var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var factory = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IHttpClientFactory>(sp);
        foreach (var name in new[] { PhotonGeocoder.HttpClientName, OsrmWalkingRouter.HttpClientName, Gtfs.GtfsDatasetProvider.HttpClientName, OpenStreetMap.OverpassQueryRunner.HttpClientName })
        {
            var agent = factory.CreateClient(name).DefaultRequestHeaders.UserAgent.ToString();
            Assert.Equal(DependencyInjection.UpstreamUserAgent, agent);
            Assert.Contains("https://opendata.al.mt", agent);
        }
    }
}
