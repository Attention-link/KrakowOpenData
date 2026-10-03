using KrakowOpenData.Domain.UrbanSpace;

namespace KrakowOpenData.Domain.Tests;

public class StreetLightTests
{
    [Theory]
    [InlineData("led", null, StreetLightTechnology.Led)]
    [InlineData("LED", null, StreetLightTechnology.Led)]
    [InlineData("led;solar_lamp", null, StreetLightTechnology.Led)]
    [InlineData("sodium_vapor", null, StreetLightTechnology.Sodium)]
    [InlineData("electric", "LED", StreetLightTechnology.Led)]
    [InlineData("electric", null, StreetLightTechnology.Unknown)]
    [InlineData(null, null, StreetLightTechnology.Unknown)]
    [InlineData("floodlight", null, StreetLightTechnology.Other)]
    public void Technology_is_read_from_lamp_type_and_light_method(string? lampType, string? lightMethod, StreetLightTechnology expected) =>
        Assert.Equal(expected, StreetLight.TechnologyFrom(lampType, lightMethod));
}
