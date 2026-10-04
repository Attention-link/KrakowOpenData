using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Tests;

/// <summary>A router that answers with the paths it was given for the direct request, and nothing for via-point requests.</summary>
public sealed class ScriptedRouter(params RoutePath[] paths) : IWalkingRouter
{
    public Task<IReadOnlyList<RoutePath>> RouteAsync(IReadOnlyList<GeoPoint> waypoints, bool alternatives, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RoutePath>>(waypoints.Count == 2 ? paths : []);
}

public sealed class DownRouter : IWalkingRouter
{
    public Task<IReadOnlyList<RoutePath>> RouteAsync(IReadOnlyList<GeoPoint> waypoints, bool alternatives, CancellationToken ct = default) =>
        throw new HttpRequestException("routing is down");
}

public class PathSamplerTests
{
    [Fact]
    public void Samples_include_both_ends_and_are_about_50_m_apart()
    {
        var a = SafetyWorld.Centre;
        var b = SafetyWorld.Offset(a, 0, 1000);
        var samples = PathSampler.Sample([a, b], 50);

        Assert.Equal(a, samples[0]);
        Assert.Equal(b, samples[^1]);
        Assert.InRange(samples.Count, 20, 22);
        for (var i = 1; i < samples.Count; i++) Assert.InRange(GridSpec.Distance(samples[i - 1], samples[i]), 40, 60);
    }

    [Fact]
    public void Samples_follow_the_bends_of_the_path()
    {
        var a = SafetyWorld.Centre;
        var corner = SafetyWorld.Offset(a, 0, 500);
        var b = SafetyWorld.Offset(corner, 500, 0);
        var samples = PathSampler.Sample([a, corner, b], 50);

        // 1000 m walked: some sample sits at the corner (within a step), none cuts across the diagonal.
        Assert.Contains(samples, s => GridSpec.Distance(s, corner) < 30);
        Assert.All(samples, s => Assert.True(GridSpec.Distance(s, a) + GridSpec.Distance(s, b) < 1000 * 1.01 + 1));
    }
}

public class RouteServiceTests
{
    // The unlit, unserved stretch 2 km north (Remote) versus the lit, well-served block at the centre.
    private static readonly GeoPoint Far = SafetyWorld.Remote;
    private static readonly GeoPoint FarEnd = SafetyWorld.Offset(Far, 0, 600);

    private static RoutePath Unserved() => new([Far, FarEnd], 600);

    private static RoutePath Served(double length)
    {
        var a = SafetyWorld.Offset(SafetyWorld.Centre, 0, -150);
        var b = SafetyWorld.Offset(SafetyWorld.Centre, 0, 150);
        return new RoutePath([a, b], length);   // geometry is in the lit block; the length is what the router reported
    }

    private static RouteService Service(IWalkingRouter router) => new(new SafetyWorld().Scores(), router);

    [Fact]
    public async Task A_safer_route_is_offered_for_night_when_it_scores_clearly_higher()
    {
        var routes = await Service(new ScriptedRouter(Unserved(), Served(700))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.Equal("street", routes.Source);
        Assert.Equal(600, routes.Fastest.LengthMeters);
        Assert.NotNull(routes.Better);
        Assert.Equal("safest", routes.BetterKind);
        Assert.True(routes.Better!.Average > routes.Fastest.Average + 10);
        Assert.Equal(100, routes.ExtraMeters);
        Assert.Equal(1, routes.ExtraMinutes);   // 8 min -> 9 min
        Assert.True(routes.ScoreGain >= RouteService.MinGain);
    }

    [Fact]
    public async Task A_cooler_route_has_a_higher_cooling_score()
    {
        var routes = await Service(new ScriptedRouter(Unserved(), Served(700))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Heat);

        Assert.Equal("coolest", routes.BetterKind);
        Assert.True(routes.Better!.Average > routes.Fastest.Average + 10, $"{routes.Better.Average} vs {routes.Fastest.Average}");
        Assert.True(routes.Fastest.Worst <= routes.Fastest.Average);   // worst = lowest, for every score
    }

    [Fact]
    public async Task A_route_that_is_too_much_longer_is_not_offered()
    {
        var routes = await Service(new ScriptedRouter(Unserved(), Served(2000))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.Null(routes.Better);
        Assert.Equal("none", routes.BetterKind);
    }

    [Fact]
    public async Task When_the_fastest_route_is_also_the_best_no_alternative_is_offered()
    {
        var routes = await Service(new ScriptedRouter(Served(500), new RoutePath([Far, FarEnd], 650))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.Equal(500, routes.Fastest.LengthMeters);
        Assert.Null(routes.Better);
        Assert.Contains("fastest route is also the best", routes.Note);
    }

    [Fact]
    public async Task Without_street_routing_only_a_straight_line_check_is_returned()
    {
        var routes = await Service(new DownRouter()).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.Equal("straight-line", routes.Source);
        Assert.Null(routes.Better);
        Assert.Contains("straight-line", routes.Note);
        Assert.True(routes.Fastest.Samples.Count > 5);
    }

    [Fact]
    public async Task Routes_over_5_km_are_rejected()
    {
        var far = SafetyWorld.Offset(Far, 0, 6000);
        await Assert.ThrowsAsync<SafetyValidationException>(() => Service(new ScriptedRouter()).GetRoutesAsync(Far, far, PlanningEvent.Night));
    }

    [Fact]
    public void Via_points_sit_either_side_of_the_middle_of_the_line()
    {
        var a = SafetyWorld.Centre;
        var b = SafetyWorld.Offset(a, 0, 1000);
        var vias = RouteService.ViaPoints(a, b, 1000);

        Assert.Equal(4, vias.Count);
        Assert.Contains(vias, v => v.Latitude > a.Latitude + 0.001);
        Assert.Contains(vias, v => v.Latitude < a.Latitude - 0.001);
        Assert.All(vias, v => Assert.InRange(v.Longitude, a.Longitude, b.Longitude));
    }
}

public class MethodServiceTests
{
    [Fact]
    public async Task The_method_documents_both_scores_with_weights_bands_and_sources()
    {
        var world = new SafetyWorld();
        var method = await new MethodService(world.Models(), world.Conditions()).GetAsync();

        var heat = method.Layers.Single(l => l.Layer == "Heat");
        var safety = method.Layers.Single(l => l.Layer == "Safety");
        Assert.StartsWith("Higher = cooler", heat.Direction);
        Assert.StartsWith("Higher = safer", safety.Direction);
        Assert.Equal(100, heat.Factors.Sum(f => f.Weight));
        Assert.Equal(100, safety.Factors.Sum(f => f.Weight));
        Assert.All(heat.Factors.Concat(safety.Factors), f => Assert.False(string.IsNullOrWhiteSpace(f.WhyWeighted) || string.IsNullOrWhiteSpace(f.Source)));
        Assert.Equal(4, heat.Bands.Count);

        // Missing shade is weighted more heavily than missing drinking water; lighting weighs most at night.
        Assert.True(heat.Factors.Single(f => f.Key == "green").Weight > heat.Factors.Single(f => f.Key == "water").Weight);
        Assert.Equal("lighting", safety.Factors.MaxBy(f => f.Weight)!.Key);
        Assert.Contains(method.Kpis, k => k.Key == "criticalCells");
    }

    [Fact]
    public async Task Mapped_counts_come_from_the_model()
    {
        var world = new SafetyWorld();
        var method = await new MethodService(world.Models(), world.Conditions()).GetAsync();

        Assert.Equal(1, method.Layers.Single(l => l.Layer == "Heat").Factors.Single(f => f.Key == "water").MappedCount);
        Assert.Equal(256, method.Layers.Single(l => l.Layer == "Safety").Factors.Single(f => f.Key == "lighting").MappedCount);
    }
}
