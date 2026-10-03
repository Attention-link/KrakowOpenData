using KrakowOpenData.Infrastructure.Imgw;

namespace KrakowOpenData.Infrastructure.Tests;

public class ImgwJsonParserTests
{
    [Fact]
    public void Parses_single_synop_object_with_string_numbers()
    {
        const string json = """
        {"id_stacji":"12566","stacja":"Kraków","data_pomiaru":"2026-10-03","godzina_pomiaru":"6",
         "temperatura":"9.4","predkosc_wiatru":"2","kierunek_wiatru":"250","wilgotnosc_wzgledna":"87.1",
         "suma_opadu":"0","cisnienie":"1018.2"}
        """;

        var w = Assert.Single(ImgwJsonParser.ParseSynop(json));

        Assert.Equal("12566", w.Id);
        Assert.Equal(9.4, w.TemperatureC);
        Assert.Equal(250, w.WindDirectionDegrees);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero), w.ObservedAt);
        Assert.False(w.IsHot);
    }

    [Fact]
    public void Synop_tolerates_nulls_and_arrays()
    {
        const string json = """
        [{"id_stacji":"1","stacja":"A","data_pomiaru":"2026-08-05","godzina_pomiaru":"14","temperatura":"39.0","cisnienie":null},
         {"id_stacji":"2","stacja":"B"}]
        """;

        var result = ImgwJsonParser.ParseSynop(json);

        var hot = Assert.Single(result); // second element has no date and is skipped
        Assert.True(hot.IsHot);
        Assert.Null(hot.PressureHPa);
    }

    [Fact]
    public void Hydro_keeps_only_matching_stations_and_reads_coordinates()
    {
        const string json = """
        [{"id_stacji":"150190340","stacja":"KRAKÓW-BIELANY","rzeka":"Wisła","stan_wody":"230","stan_wody_data_pomiaru":"2026-10-03 06:00:00","lat":"50.04","lon":"19.84"},
         {"id_stacji":"999","stacja":"TARNÓW","rzeka":"Biała","stan_wody":"100"},
         {"id_stacji":"777","stacja":"PROSZOWICE","rzeka":"Szreniawa","stan_wody":"80"}]
        """;

        var result = ImgwJsonParser.ParseHydro(json, ["KRAK"], ["777"]);

        Assert.Equal(new[] { "150190340", "777" }, result.Select(r => r.Id).ToArray());
        var bielany = result[0];
        Assert.Equal(230, bielany.WaterLevelCm);
        Assert.Equal("Wisła", bielany.River);
        Assert.NotNull(bielany.Location);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero), bielany.MeasuredAt);
    }

    [Fact]
    public void Warnings_map_level_probability_window_and_teryt()
    {
        const string json = """
        [{"id":"w1","nazwa_zdarzenia":"Upał","stopien":"2","prawdopodobienstwo":"80",
          "obowiazuje_od":"2026-08-05 08:00:00","obowiazuje_do":"2026-08-06 20:00:00",
          "tresc":"Temperatura maksymalna od 34°C do 36°C.","komentarz":"","teryt":["1261","1206"]}]
        """;

        var w = Assert.Single(ImgwJsonParser.ParseWarnings(json));

        Assert.Equal("Upał", w.EventName);
        Assert.Equal(2, w.Level);
        Assert.Equal(80, w.ProbabilityPercent);
        Assert.Equal("Temperatura maksymalna od 34°C do 36°C.", w.Content);
        Assert.True(w.AppliesTo("1261"));
        Assert.True(w.IsActiveAt(new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Decimal_commas_are_accepted()
    {
        const string json = """{"id_stacji":"1","stacja":"A","data_pomiaru":"2026-10-03","godzina_pomiaru":"6","temperatura":"9,4"}""";
        Assert.Equal(9.4, Assert.Single(ImgwJsonParser.ParseSynop(json)).TemperatureC);
    }
}
