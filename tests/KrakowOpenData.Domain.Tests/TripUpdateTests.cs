using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Domain.Tests;

public class TripUpdateTests
{
    private static StopTimeUpdate Update(int seq, int? delay, bool skipped = false) =>
        new($"stop-{seq}", seq, delay, delay, null, null, skipped);

    private static TripUpdate Trip(int? tripDelay, params StopTimeUpdate[] updates) =>
        new("tu", "trip", "route", tripDelay, updates, null, "T");

    [Fact]
    public void Exact_stop_update_is_used()
    {
        var trip = Trip(null, Update(3, 120));
        Assert.Equal(120, trip.DelayAtStop("stop-3", 3));
    }

    [Fact]
    public void Delay_propagates_downstream_from_the_latest_upstream_update()
    {
        var trip = Trip(null, Update(2, 60), Update(5, 240));
        Assert.Equal(60, trip.DelayAtStop("stop-4", 4));
        Assert.Equal(240, trip.DelayAtStop("stop-9", 9));
    }

    [Fact]
    public void Falls_back_to_trip_level_delay_before_any_stop_update()
    {
        var trip = Trip(30, Update(5, 240));
        Assert.Equal(30, trip.DelayAtStop("stop-1", 1));
    }

    [Fact]
    public void Returns_null_when_nothing_is_known()
    {
        Assert.Null(Trip(null).DelayAtStop("stop-1", 1));
    }

    [Fact]
    public void Departure_delay_is_preferred_over_arrival_delay()
    {
        var update = new StopTimeUpdate("s", 1, ArrivalDelaySeconds: 30, DepartureDelaySeconds: 90, null, null, false);
        Assert.Equal(90, update.EffectiveDelaySeconds);
    }

    [Fact]
    public void Skipped_stop_is_reported()
    {
        var trip = Trip(null, Update(3, null, skipped: true));
        Assert.True(trip.IsStopSkipped("stop-3", 3));
        Assert.False(trip.IsStopSkipped("stop-4", 4));
    }
}
