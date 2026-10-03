using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Tests;

public class GeoPointTests
{
    private static readonly GeoPoint MainSquare = new(50.0617, 19.9373);
    private static readonly GeoPoint MainStation = new(50.0662, 19.9455);

    [Fact]
    public void DistanceTo_same_point_is_zero()
    {
        Assert.Equal(0, MainSquare.DistanceTo(MainSquare), precision: 6);
    }

    [Fact]
    public void DistanceTo_main_square_to_main_station_is_about_750_metres()
    {
        var distance = MainSquare.DistanceTo(MainStation);
        Assert.InRange(distance, 700, 800);
    }

    [Fact]
    public void DistanceTo_is_symmetric()
    {
        Assert.Equal(MainSquare.DistanceTo(MainStation), MainStation.DistanceTo(MainSquare), precision: 6);
    }

    [Theory]
    [InlineData("50.0617,19.9373", true)]
    [InlineData(" 50.0617 , 19.9373 ", true)]
    [InlineData("91,19", false)]
    [InlineData("50.06", false)]
    [InlineData("abc,def", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_accepts_only_valid_lat_lon(string? input, bool expected)
    {
        Assert.Equal(expected, GeoPoint.TryParse(input, out _));
    }

    [Fact]
    public void Origin_is_treated_as_invalid_missing_coordinates()
    {
        Assert.False(new GeoPoint(0, 0).IsValid);
    }

    [Fact]
    public void ToString_uses_invariant_culture()
    {
        Assert.Equal("50.0617,19.9373", MainSquare.ToString());
    }
}
