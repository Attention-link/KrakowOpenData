using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.DataSources;
using KrakowOpenData.Infrastructure.Repositories;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Infrastructure.Tests;

public class CachingAndSourcesTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Cache_reuses_value_within_ttl_and_refreshes_after()
    {
        var calls = 0;
        var cache = new RefreshingCache<int>(_ => Task.FromResult(++calls), TimeSpan.FromMinutes(5), _clock);

        Assert.Equal(1, await cache.GetAsync());
        Assert.Equal(1, await cache.GetAsync());

        _clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(2, await cache.GetAsync());
    }

    [Fact]
    public async Task Cache_serves_stale_value_when_refresh_fails()
    {
        var fail = false;
        var cache = new RefreshingCache<string>(
            _ => fail ? throw new HttpRequestException("down") : Task.FromResult("good"),
            TimeSpan.FromMinutes(1), _clock);

        Assert.Equal("good", await cache.GetAsync());
        fail = true;
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("good", await cache.GetAsync());
        Assert.IsType<HttpRequestException>(cache.LastError);
    }

    [Fact]
    public async Task Cache_throws_when_first_load_fails()
    {
        var cache = new RefreshingCache<string>(_ => throw new HttpRequestException("down"), TimeSpan.FromMinutes(1), _clock);
        await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync());
    }

    [Fact]
    public async Task Concurrent_callers_share_one_refresh()
    {
        var calls = 0;
        var gate = new TaskCompletionSource();
        var cache = new RefreshingCache<int>(async _ =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return 42;
        }, TimeSpan.FromMinutes(5), _clock);

        var first = cache.GetAsync();
        var second = cache.GetAsync();
        gate.SetResult();

        Assert.Equal(new[] { 42, 42 }, await Task.WhenAll(first, second));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Composite_skips_a_failing_source()
    {
        var ok = new DelegateDataSource<int>(_ => Task.FromResult<IReadOnlyList<int>>([1, 2]));
        var broken = new DelegateDataSource<int>(_ => throw new HttpRequestException("down"));

        var result = await new CompositeDataSource<int>([broken, ok]).LoadAsync();

        Assert.Equal(new[] { 1, 2 }, result.ToArray());
    }

    [Fact]
    public async Task Composite_throws_when_every_source_fails()
    {
        var broken = new DelegateDataSource<int>(_ => throw new HttpRequestException("down"));
        await Assert.ThrowsAsync<AggregateException>(() => new CompositeDataSource<int>([broken, broken]).LoadAsync());
    }

    [Fact]
    public async Task Repository_filters_with_specifications_and_finds_by_id_case_insensitively()
    {
        var source = new DelegateDataSource<District>(_ => Task.FromResult<IReadOnlyList<District>>(
        [
            new District("I", 1, "Stare Miasto", null, null, "t"),
            new District("IV", 4, "Prądnik Biały", 74105, null, "t")
        ]));
        var repo = new Repository<District>(source);

        Assert.Equal("Prądnik Biały", (await repo.GetByIdAsync("iv"))?.Name);
        Assert.Equal(1, await repo.CountAsync(new Specification<District>(d => d.RegisteredPopulation > 0)));
        Assert.Equal(2, (await repo.ListAsync()).Count);
    }
}
