using System.Net;
using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.OpenStreetMap;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Tests;

public class BackgroundDatasetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Saved_copy_is_served_without_waiting_for_the_source()
    {
        var store = new MemoryStore { ["lamps"] = ("from-disk", Now.AddHours(-1)) };
        var downloads = 0;
        var dataset = Create(store, _ => { downloads++; return Task.FromResult("fresh"); });

        Assert.Equal("from-disk", await dataset.GetAsync(CancellationToken.None));
        Assert.Equal(0, downloads); // an hour old is fresh enough (refresh is daily)
    }

    [Fact]
    public async Task First_load_that_is_too_slow_answers_still_loading()
    {
        var release = new TaskCompletionSource<string>();
        var dataset = Create(new MemoryStore(), _ => release.Task, firstLoadWait: TimeSpan.FromMilliseconds(50));

        var ex = await Assert.ThrowsAsync<DataSourceLoadingException>(() => dataset.GetAsync(CancellationToken.None));
        Assert.Equal("Street lights", ex.Dataset);

        release.SetResult("done");
        await Task.Delay(100);
        Assert.Equal("done", await dataset.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Successful_download_is_saved_for_the_next_start()
    {
        var store = new MemoryStore();
        var dataset = Create(store, _ => Task.FromResult("downloaded"));

        Assert.Equal("downloaded", await dataset.GetAsync(CancellationToken.None));
        await Task.Delay(50);
        Assert.Equal("downloaded", store["lamps"].Content);
    }

    [Fact]
    public async Task Failed_refresh_keeps_the_previous_copy()
    {
        var clock = new FakeClock(Now);
        var store = new MemoryStore { ["lamps"] = ("old", Now.AddDays(-2)) }; // stale: a refresh starts
        var dataset = Create(store, _ => throw new HttpRequestException("down"), clock: clock);

        Assert.Equal("old", await dataset.GetAsync(CancellationToken.None));
        await Task.Delay(100);
        Assert.Equal("old", await dataset.GetAsync(CancellationToken.None));
        Assert.IsType<HttpRequestException>(dataset.LastError);
    }

    [Fact]
    public async Task First_load_failure_is_reported_as_the_source_error()
    {
        var dataset = Create(new MemoryStore(), _ => Task.FromException<string>(new HttpRequestException("down")));
        await Assert.ThrowsAsync<HttpRequestException>(() => dataset.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Overpass_runner_falls_back_to_the_next_mirror()
    {
        var handler = new RoutingHandler(new()
        {
            ["https://busy.example/api"] = HttpStatusCode.TooManyRequests,
            ["https://ok.example/api"] = HttpStatusCode.OK
        });
        var options = Microsoft.Extensions.Options.Options.Create(new KrakowDataOptions { OverpassUrls = ["https://busy.example/api", "https://ok.example/api"] });
        var runner = new OverpassQueryRunner(new SingleClientFactory(new HttpClient(handler)), options, NullLogger<OverpassQueryRunner>.Instance);

        var result = await runner.RunAsync("[out:json];", CancellationToken.None);

        Assert.Equal("{\"elements\":[]}", result);
        Assert.Equal(new[] { "https://busy.example/api", "https://ok.example/api" }, handler.Calls);
    }

    private static BackgroundDataset<string> Create(
        ISnapshotStore store, Func<CancellationToken, Task<string>> download, TimeSpan? firstLoadWait = null, IClock? clock = null) =>
        new("lamps", "Street lights", download, s => s, store, clock ?? new FakeClock(Now), TimeSpan.FromDays(1),
            firstLoadWait ?? TimeSpan.FromSeconds(5), NullLogger.Instance);

    private sealed class MemoryStore : Dictionary<string, (string Content, DateTimeOffset SavedAt)>, ISnapshotStore
    {
        public Task<(string Content, DateTimeOffset SavedAt)?> LoadAsync(string name, CancellationToken ct) =>
            Task.FromResult<(string, DateTimeOffset)?>(TryGetValue(name, out var v) ? v : null);

        public Task SaveAsync(string name, string content, CancellationToken ct)
        {
            this[name] = (content, Now);
            return Task.CompletedTask;
        }
    }

    private sealed class RoutingHandler(Dictionary<string, HttpStatusCode> statuses) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Calls.Add(url);
            return Task.FromResult(new HttpResponseMessage(statuses[url]) { Content = new StringContent("{\"elements\":[]}") });
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
