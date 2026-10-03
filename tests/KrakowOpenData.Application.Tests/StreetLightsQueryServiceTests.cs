using KrakowOpenData.Application.Services;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Application.Tests;

public class StreetLightsQueryServiceTests
{
    private static readonly GeoPoint Square = new(50.0617, 19.9373);

    private static StreetLightsQueryService Service() => new(new InMemoryRepository<StreetLight>(
        Light("near-led", 50.0618, StreetLightTechnology.Led, "bent_mast"),
        Light("mid-sodium", 50.0630, StreetLightTechnology.Sodium, "bent_mast"),
        Light("mid-unknown", 50.0635, StreetLightTechnology.Unknown, null),
        Light("far", 50.0900, StreetLightTechnology.Led, "wall")));

    [Fact]
    public async Task Lamps_near_a_point_are_nearest_first_within_radius()
    {
        var result = await Service().GetAsync(Square, 500, null, 100);

        Assert.Equal(new[] { "near-led", "mid-sodium", "mid-unknown" }, result.Select(l => l.Id).ToArray());
        Assert.True(result[0].DistanceMeters < result[1].DistanceMeters);
    }

    [Fact]
    public async Task Technology_filter_applies()
    {
        var result = await Service().GetAsync(null, 0, StreetLightTechnology.Led, 100);
        Assert.Equal(new[] { "near-led", "far" }, result.Select(l => l.Id).ToArray());
    }

    [Fact]
    public async Task Summary_counts_technologies_mounts_and_density()
    {
        var summary = await Service().GetSummaryAsync(Square, 500);

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.WithKnownTechnology);
        Assert.Equal(1, summary.ByTechnology["Led"]);
        Assert.Equal(2, summary.ByMount["bent_mast"]);
        Assert.Equal(1, summary.ByMount["unknown"]);
        Assert.Equal(0.785, summary.AreaKm2);
        Assert.Equal(3.8, summary.PerKm2);
    }

    [Fact]
    public async Task Citywide_summary_has_no_area()
    {
        var summary = await Service().GetSummaryAsync(null, 0);
        Assert.Equal(4, summary.Total);
        Assert.Null(summary.PerKm2);
    }

    private static StreetLight Light(string id, double lat, StreetLightTechnology tech, string? mount) =>
        new(id, new GeoPoint(lat, 19.9373), tech, mount, null, null, null, null, "OSM");
}
