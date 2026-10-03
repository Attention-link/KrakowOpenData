using KrakowOpenData.Domain.ClimateAndCrisis;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Environment;
using KrakowOpenData.Domain.Mobility;
using KrakowOpenData.Domain.PublicServices;
using KrakowOpenData.Domain.UrbanSpace;
using KrakowOpenData.Infrastructure.DataSources;

namespace KrakowOpenData.Api.Tests.Fakes;

/// <summary>Small in-memory datasets that replace the live sources in API tests (no network).</summary>
public static class FakeData
{
    private const string Source = "TEST";

    public static IDataSource<T> Of<T>(params T[] items) =>
        new DelegateDataSource<T>(_ => Task.FromResult<IReadOnlyList<T>>(items));

    public static WeatherObservation[] Weather { get; } =
    [
        new("12566", "Kraków", DateTimeOffset.UtcNow.AddHours(-1), 14.2, 3, 250, 72, 0, 1016, Source)
    ];

    public static AirQualityMeasurement[] AirQuality { get; } =
    [
        new("gios-400", "Kraków, Aleja Krasińskiego", new GeoPoint(50.0577, 19.9262), DateTimeOffset.UtcNow, 32, 21, 48, Source, "Umiarkowany"),
        new("gios-401", "Kraków, ul. Bujaka", new GeoPoint(50.0109, 19.9496), DateTimeOffset.UtcNow, 18, 9, 20, Source, "Dobry")
    ];

    public static HydroObservation[] RiverGauges { get; } =
    [
        new("150190340", "KRAKÓW-BIELANY", "Wisła", new GeoPoint(50.04, 19.84), 410, DateTimeOffset.UtcNow, 370, 520, null, Source),
        new("150190130", "OJCÓW", "Prądnik", new GeoPoint(50.20, 19.83), 90, DateTimeOffset.UtcNow, 150, 180, null, Source)
    ];

    public static WeatherWarning[] Warnings { get; } =
    [
        new("w1", "Silny wiatr", 1, 80, null, null, "Test warning.", ["1261"], Source),
        new("w2", "Upał", 2, 90, null, null, "Elsewhere.", ["0201"], Source)
    ];

    public static ParkAndRideFacility[] ParkAndRide { get; } =
    [
        new("osm-w483476337", "P+R Kurdwanów", "Porucznika Jerzego Halszki 1, Kraków", new GeoPoint(50.0131, 19.9486), 167, 4, null, "Mo-Su 04:30-02:30", "fee: yes", Source)
    ];

    public static District[] Districts { get; } =
        new[] { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII" }
            .Select((roman, i) => new District(roman, i + 1, $"District {roman}", 10000 + i, null, Source))
            .ToArray();

    public static CityServiceCard[] ServiceCards { get; } =
    [
        new("GD-35", "Pobieranie otwartych danych przestrzennych z portalu MSIP", "GD", "wersja 3", [], null, null, null, null, null,
            "https://www.bip.krakow.pl/uslugi/GD-35", []),
        new("AM-13", "Opiniowanie wniosków dotyczących murali", "AM", "wersja 8", [], null, null, null, null, null,
            "https://www.bip.krakow.pl/uslugi/AM-13", [])
    ];

    public static StreetLight[] StreetLights { get; } =
    [
        new("osm-n10", new GeoPoint(50.0618, 19.9374), StreetLightTechnology.Led, "bent_mast", 1, 8, "Tauron", "A1", Source),
        new("osm-n11", new GeoPoint(50.0625, 19.9380), StreetLightTechnology.Unknown, null, null, null, null, null, Source),
        new("osm-n12", new GeoPoint(50.0900, 19.9000), StreetLightTechnology.Sodium, "wall", null, null, null, null, Source)
    ];

    public static Amenity[] Amenities { get; } =
    [
        new("osm-n1", AmenityKind.Defibrillator, "AED Rynek", new GeoPoint(50.0618, 19.9374), "access: yes", Source),
        new("osm-n2", AmenityKind.DrinkingWater, null, new GeoPoint(50.0640, 19.9400), null, Source)
    ];
}
