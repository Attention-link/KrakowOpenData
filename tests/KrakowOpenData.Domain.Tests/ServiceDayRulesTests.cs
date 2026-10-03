using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Domain.Tests;

public class ServiceDayRulesTests
{
    private static readonly ServiceCalendar Weekdays = new(
        "wk",
        new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
        new DateOnly(2026, 9, 1),
        new DateOnly(2026, 12, 31));

    private static readonly DateOnly Friday = new(2026, 10, 2);
    private static readonly DateOnly Saturday = new(2026, 10, 3);

    [Fact]
    public void Weekly_pattern_applies_inside_the_date_range()
    {
        Assert.True(ServiceDayRules.IsActiveOn(Friday, Weekdays, null));
        Assert.False(ServiceDayRules.IsActiveOn(Saturday, Weekdays, null));
    }

    [Fact]
    public void Outside_the_date_range_the_service_does_not_run()
    {
        Assert.False(ServiceDayRules.IsActiveOn(new DateOnly(2027, 1, 4), Weekdays, null));
    }

    [Fact]
    public void Removed_exception_wins_over_the_weekly_pattern()
    {
        var holiday = new[] { new ServiceException("wk", Friday, IsAdded: false) };
        Assert.False(ServiceDayRules.IsActiveOn(Friday, Weekdays, holiday));
    }

    [Fact]
    public void Added_exception_wins_over_the_weekly_pattern()
    {
        var extra = new[] { new ServiceException("wk", Saturday, IsAdded: true) };
        Assert.True(ServiceDayRules.IsActiveOn(Saturday, Weekdays, extra));
    }

    [Fact]
    public void Service_without_calendar_runs_only_on_added_dates()
    {
        var added = new[] { new ServiceException("x", Saturday, IsAdded: true) };
        Assert.True(ServiceDayRules.IsActiveOn(Saturday, null, added));
        Assert.False(ServiceDayRules.IsActiveOn(Friday, null, added));
    }

    [Theory]
    [InlineData(0, TransportMode.Tram)]
    [InlineData(3, TransportMode.Bus)]
    [InlineData(2, TransportMode.Rail)]
    [InlineData(900, TransportMode.Tram)]
    [InlineData(704, TransportMode.Bus)]
    [InlineData(42, TransportMode.Other)]
    public void Gtfs_route_types_map_to_modes(int routeType, TransportMode expected)
    {
        Assert.Equal(expected, TransportModeExtensions.FromGtfsRouteType(routeType));
    }
}
