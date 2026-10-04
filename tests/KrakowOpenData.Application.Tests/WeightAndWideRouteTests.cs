using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Safety;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Tests;

public class FactorWeightTests
{
    [Theory]
    [InlineData(new double[] { 25, 35, 20, 10, 10 }, new double[] { 25, 35, 20, 10, 10 })]
    [InlineData(new double[] { 3, 1, 1, 0, 0 }, new double[] { 60, 20, 20, 0, 0 })]
    [InlineData(new double[] { 1, 1, 1 }, new double[] { 34, 33, 33 })]   // the remainder goes to the first
    [InlineData(new double[] { 0, 0, 5 }, new double[] { 0, 0, 100 })]
    public void Weights_are_scaled_to_exactly_100(double[] typed, double[] expected)
    {
        var scaled = FactorWeights.Normalise(typed);
        Assert.Equal(expected, scaled);
        Assert.Equal(100, scaled.Sum());
    }

    [Fact]
    public void All_zero_weights_are_rejected() =>
        Assert.Throws<ArgumentException>(() => FactorWeights.Normalise([0, 0, 0]));

    [Fact]
    public void Layers_without_saved_weights_keep_the_defaults() =>
        Assert.Same(SafetyModel.HeatFactors, FactorWeights.Apply(Layer.Heat, new Dictionary<string, double> { ["lighting"] = 50 }));
}

public class WeightServiceTests
{
    private static (SafetyWorld World, WeightService Weights, ScoreService Scores) Setup()
    {
        var world = new SafetyWorld();
        var models = new SafetyModelProvider(world.Lights, world.Amenities, world.PlacesForModel, world.Stops, world.Schedule, world.Clock, world.Store);
        var method = new MethodService(models, world.Conditions());
        var scores = new ScoreService(models, world.Conditions(), world.Store, world.Clock);
        return (world, new WeightService(method, models, world.Store), scores);
    }

    [Fact]
    public async Task Weights_start_at_the_defaults_with_reasons()
    {
        var (_, weights, _) = Setup();
        var all = await weights.GetAsync();

        Assert.False(all.Customized);
        Assert.Equal(4, all.Layers.Count);
        Assert.All(all.Layers, l => Assert.Equal(100, l.Factors.Sum(f => f.Weight)));
        Assert.All(all.Layers.SelectMany(l => l.Factors), f =>
        {
            Assert.Equal(f.DefaultWeight, f.Weight);
            Assert.False(string.IsNullOrWhiteSpace(f.Why));
            Assert.False(string.IsNullOrWhiteSpace(f.Source));
        });
    }

    [Fact]
    public async Task Changed_weights_are_scaled_saved_and_change_the_scores()
    {
        var (world, weights, scores) = Setup();
        var before = await scores.GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Night);

        // Remote has no lamps and nothing nearby: it scores about 0 whatever the weights; a place with only lighting would change. Use the centre.
        var centreBefore = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night);
        var result = await weights.SetAsync(new SetWeightsRequest(new Dictionary<string, double> { ["lighting"] = 0, ["nightTransit"] = 1, ["openPlaces"] = 1, ["aed"] = 0 }));

        var safety = result.Layers.Single(l => l.Layer == "Safety");
        Assert.True(safety.Customized);
        Assert.True(result.Customized);
        Assert.Equal(50, safety.Factors.Single(f => f.Key == "nightTransit").Weight);
        Assert.Equal(0, safety.Factors.Single(f => f.Key == "lighting").Weight);
        Assert.Equal(40, safety.Factors.Single(f => f.Key == "lighting").DefaultWeight);
        Assert.Equal(100, safety.Factors.Sum(f => f.Weight));
        Assert.False(result.Layers.Single(l => l.Layer == "Heat").Customized);

        var centreAfter = await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night);
        Assert.Equal(0, centreAfter.Safety.Factors.Single(f => f.Key == "lighting").Weight);
        Assert.Equal(40, centreBefore.Safety.Factors.Single(f => f.Key == "lighting").Contribution);
        Assert.Equal(0, centreAfter.Safety.Factors.Single(f => f.Key == "lighting").Contribution);
        Assert.Equal(50, centreAfter.Safety.Factors.Single(f => f.Key == "nightTransit").Contribution);
        Assert.Equal(before.Heat.Score, (await scores.GetPlaceAsync(SafetyWorld.Remote, PlanningEvent.Night)).Heat.Score);
        Assert.Equal(1, (await world.Store.GetWeightOverridesAsync()).Count(w => w.Key == "nightTransit"));
    }

    [Fact]
    public async Task Reset_goes_back_to_the_defaults()
    {
        var (_, weights, scores) = Setup();
        var original = (await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night)).Safety.Score;
        await weights.SetAsync(new SetWeightsRequest(new Dictionary<string, double> { ["lighting"] = 0, ["nightTransit"] = 5, ["openPlaces"] = 1, ["aed"] = 1 }));
        var reset = await weights.ResetAsync();

        Assert.False(reset.Customized);
        Assert.Equal(original, (await scores.GetPlaceAsync(SafetyWorld.Centre, PlanningEvent.Night)).Safety.Score);
    }

    [Fact]
    public async Task Typing_the_default_weights_is_not_a_customization()
    {
        var (_, weights, _) = Setup();
        var result = await weights.SetAsync(new SetWeightsRequest(SafetyModel.SafetyFactors.ToDictionary(f => f.Key, f => f.Weight)));
        Assert.False(result.Customized);
    }

    [Theory]
    [InlineData("nope", 10.0)]
    [InlineData("lighting", -1.0)]
    [InlineData("lighting", double.NaN)]
    [InlineData("lighting", 5000.0)]
    public async Task Invalid_weights_are_rejected(string key, double value)
    {
        var (_, weights, _) = Setup();
        await Assert.ThrowsAsync<SafetyValidationException>(() => weights.SetAsync(new SetWeightsRequest(new Dictionary<string, double> { [key] = value })));
    }

    [Fact]
    public async Task A_layer_cannot_be_left_with_only_zero_weights()
    {
        var (_, weights, _) = Setup();
        var zeros = SafetyModel.SafetyFactors.ToDictionary(f => f.Key, _ => 0.0);
        await Assert.ThrowsAsync<SafetyValidationException>(() => weights.SetAsync(new SetWeightsRequest(zeros)));
    }

    [Fact]
    public async Task Weights_apply_to_the_method_that_residents_read()
    {
        var (world, weights, _) = Setup();
        await weights.SetAsync(new SetWeightsRequest(new Dictionary<string, double> { ["water"] = 100, ["green"] = 0, ["refuge"] = 0, ["toilets"] = 0, ["transit"] = 0 }));
        var models = new SafetyModelProvider(world.Lights, world.Amenities, world.PlacesForModel, world.Stops, world.Schedule, world.Clock, world.Store);
        var method = await new MethodService(models, world.Conditions()).GetAsync();

        var water = method.Layers.Single(l => l.Layer == "Heat").Factors.Single(f => f.Key == "water");
        Assert.Equal(100, water.Weight);
        Assert.Equal(25, water.DefaultWeight);
    }
}

public class WideRouteSearchTests
{
    private static readonly GeoPoint Far = SafetyWorld.Remote;
    private static readonly GeoPoint FarEnd = SafetyWorld.Offset(Far, 0, 600);

    private static RoutePath Unserved() => new([Far, FarEnd], 600);

    private static RoutePath Served(double length) =>
        new([SafetyWorld.Offset(SafetyWorld.Centre, 0, -150), SafetyWorld.Offset(SafetyWorld.Centre, 0, 150)], length);

    private static RouteService Service(IWalkingRouter router) => new(new SafetyWorld().Scores(), router);

    [Fact]
    public async Task A_poor_fastest_route_widens_the_search_for_a_safer_one_even_when_it_is_much_longer()
    {
        // 1300 m is more than 30 % longer than 600 m, so the normal search would drop it; the fastest route is unsafe, so it is allowed.
        var routes = await Service(new ScriptedRouter(Unserved(), Served(1300))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.True(routes.Widened);
        Assert.False(routes.FastestIsAcceptable);
        Assert.NotNull(routes.Better);
        Assert.Equal(1300, routes.Better!.LengthMeters);
        Assert.Contains("farther away", routes.Note);
    }

    [Fact]
    public async Task A_good_fastest_route_does_not_widen_the_search()
    {
        var routes = await Service(new ScriptedRouter(Served(500), Served(1300))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.False(routes.Widened);
        Assert.True(routes.FastestIsAcceptable);
        Assert.Null(routes.Better);
    }

    [Fact]
    public async Task Even_a_widened_search_stops_at_a_reasonable_detour()
    {
        var routes = await Service(new ScriptedRouter(Unserved(), Served(2500))).GetRoutesAsync(Far, FarEnd, PlanningEvent.Night);

        Assert.True(routes.Widened);
        Assert.Null(routes.Better);   // 600 m + up to 1000 m allowed; 2500 m is too far
        Assert.Contains("no street route within reach", routes.Note);
    }

    [Theory]
    [InlineData(70, 50, true)]
    [InlineData(64, 90, false)]
    [InlineData(80, 44, false)]
    [InlineData(65, 45, true)]
    public void The_fastest_route_is_acceptable_when_both_average_and_weakest_stretch_are_good_enough(double average, double worst, bool expected)
    {
        var option = new RouteOptionDto("fastest", 600, 8, [], [], average, worst, 0, 0);
        Assert.Equal(expected, RouteService.IsAcceptable(option, PlanningEvent.Night));
    }

    [Fact]
    public void For_heat_the_scale_is_turned_around()
    {
        // Heat 30 average / 50 worst = goodness 70 / 50: acceptable. Heat 40 average = goodness 60: not.
        Assert.True(RouteService.IsAcceptable(new RouteOptionDto("fastest", 600, 8, [], [], 30, 50, 0, 0), PlanningEvent.Heat));
        Assert.False(RouteService.IsAcceptable(new RouteOptionDto("fastest", 600, 8, [], [], 40, 50, 0, 0), PlanningEvent.Heat));
    }

    [Fact]
    public void Wide_via_points_swing_farther_than_the_normal_ones()
    {
        var a = SafetyWorld.Centre;
        var b = SafetyWorld.Offset(a, 0, 2000);
        var normal = RouteService.ViaPoints(a, b, 2000).Max(p => GridSpec.Distance(p, new GeoPoint((a.Latitude + b.Latitude) / 2, (a.Longitude + b.Longitude) / 2)));
        var wide = RouteService.WideViaPoints(a, b, 2000).Max(p => GridSpec.Distance(p, new GeoPoint((a.Latitude + b.Latitude) / 2, (a.Longitude + b.Longitude) / 2)));

        Assert.True(wide > normal * 1.5, $"wide {wide:0} vs normal {normal:0}");
        Assert.Equal(6, RouteService.WideViaPoints(a, b, 2000).Count);
    }
}
