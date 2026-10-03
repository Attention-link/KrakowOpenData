using KrakowOpenData.Application.Abstractions;

namespace KrakowOpenData.Application.Tests;

public class KrakowTimeTests
{
    [Fact]
    public void Service_day_offset_uses_summer_time_in_October()
    {
        var moment = KrakowTime.FromServiceDay(new DateOnly(2026, 10, 3), new TimeSpan(8, 0, 0));
        Assert.Equal(TimeSpan.FromHours(2), moment.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero), moment.ToUniversalTime());
    }

    [Fact]
    public void Service_day_offset_uses_winter_time_in_December()
    {
        var moment = KrakowTime.FromServiceDay(new DateOnly(2026, 12, 1), new TimeSpan(8, 0, 0));
        Assert.Equal(TimeSpan.FromHours(1), moment.Offset);
    }

    [Fact]
    public void Times_after_24h_roll_into_the_next_calendar_day()
    {
        var moment = KrakowTime.FromServiceDay(new DateOnly(2026, 10, 3), new TimeSpan(25, 15, 0));
        Assert.Equal(new DateTime(2026, 10, 4, 1, 15, 0), moment.DateTime);
    }

    [Fact]
    public void Local_date_follows_Krakow_midnight_not_UTC()
    {
        // 23:30 UTC on 3 Oct is 01:30 on 4 Oct in Kraków.
        Assert.Equal(new DateOnly(2026, 10, 4), KrakowTime.LocalDate(new DateTimeOffset(2026, 10, 3, 23, 30, 0, TimeSpan.Zero)));
    }
}
