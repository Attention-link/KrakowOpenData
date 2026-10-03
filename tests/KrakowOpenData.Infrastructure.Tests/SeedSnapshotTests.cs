using KrakowOpenData.Infrastructure.Common;
using KrakowOpenData.Infrastructure.OpenStreetMap;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace KrakowOpenData.Infrastructure.Tests;

/// <summary>
/// The app must work on a machine that has never downloaded the OpenStreetMap data: the snapshots bundled in the assembly are
/// what it starts from. These tests make sure they are really there, are readable by the real parsers, and are used only when
/// nothing has been saved on the machine.
/// </summary>
public class SeedSnapshotTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "krk-seed-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private FileSnapshotStore StoreIn(string folder) =>
        new(Microsoft.Extensions.Options.Options.Create(new KrakowDataOptions { CacheDirectory = folder }), NullLogger<FileSnapshotStore>.Instance);

    [Fact]
    public void The_three_openstreetmap_snapshots_are_bundled()
    {
        Assert.Equal(["osm-amenities", "osm-safety-places", "osm-street-lights"], SeedSnapshots.Names.OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task The_bundled_street_lamps_are_read_by_the_real_parser()
    {
        var seed = await SeedSnapshots.LoadAsync("osm-street-lights", CancellationToken.None);
        var lamps = StreetLightParser.Parse(seed!.Value.Content);
        Assert.InRange(lamps.Count, 20_000, 60_000);   // about 27,000 are mapped for Kraków
    }

    [Fact]
    public async Task The_bundled_amenities_and_places_are_read_by_the_real_parsers()
    {
        var amenities = OverpassParser.Parse((await SeedSnapshots.LoadAsync("osm-amenities", CancellationToken.None))!.Value.Content);
        Assert.NotEmpty(amenities.Amenities);
        Assert.Contains(amenities.Amenities, a => a.Kind.ToString() == "DrinkingWater");

        var places = SafetyPlacesParser.Parse((await SeedSnapshots.LoadAsync("osm-safety-places", CancellationToken.None))!.Value.Content);
        Assert.Contains(places, p => p.Kind.ToString() == "Park");
        Assert.Contains(places, p => p.Kind.ToString() == "Pharmacy");
    }

    [Fact]
    public async Task A_machine_with_nothing_saved_starts_from_the_bundled_snapshot_dated_so_a_refresh_starts_at_once()
    {
        var saved = await StoreIn(Path.Combine(_folder, "empty")).LoadAsync("osm-street-lights", CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(SeedSnapshots.TakenAt, saved!.Value.SavedAt);
        Assert.True(DateTimeOffset.UtcNow - saved.Value.SavedAt > TimeSpan.FromDays(1), "older than the daily refresh, so fresh data replaces it");
    }

    [Fact]
    public async Task A_copy_saved_on_the_machine_wins_over_the_bundled_one()
    {
        var store = StoreIn(_folder);
        await store.SaveAsync("osm-street-lights", "[]", CancellationToken.None);

        var loaded = await store.LoadAsync("osm-street-lights", CancellationToken.None);

        Assert.Equal("[]", loaded!.Value.Content);
        Assert.NotEqual(SeedSnapshots.TakenAt, loaded.Value.SavedAt);
    }

    [Fact]
    public async Task A_dataset_without_a_bundled_snapshot_is_simply_missing()
    {
        Assert.Null(await StoreIn(_folder).LoadAsync("no-such-dataset", CancellationToken.None));
        Assert.Null(await SeedSnapshots.LoadAsync("no-such-dataset", CancellationToken.None));
    }
}
