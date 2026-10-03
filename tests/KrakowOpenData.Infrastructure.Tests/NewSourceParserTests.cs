using KrakowOpenData.Infrastructure.Gios;
using KrakowOpenData.Infrastructure.Imgw;
using KrakowOpenData.Infrastructure.Nfz;
using KrakowOpenData.Infrastructure.OpenDataPortal;

namespace KrakowOpenData.Infrastructure.Tests;

public class NewSourceParserTests
{
    [Fact]
    public void Gios_stations_are_filtered_by_city_ignoring_diacritics()
    {
        const string json = """
        {"Lista stacji pomiarowych":[
          {"Identyfikator stacji":400,"Nazwa stacji":"Kraków, Aleja Krasińskiego","WGS84 φ N":"50.057678","WGS84 λ E":"19.926189","Nazwa miasta":"Kraków"},
          {"Identyfikator stacji":1,"Nazwa stacji":"Tarnów","WGS84 φ N":"50.0","WGS84 λ E":"20.9","Nazwa miasta":"Tarnów"}]}
        """;

        var station = Assert.Single(GiosJsonParser.ParseStations(json, "krakow"));
        Assert.Equal(400, station.Id);
        Assert.Equal(50.057678, station.Location!.Value.Latitude, precision: 5);
    }

    [Fact]
    public void Gios_sensors_latest_value_and_index_are_read()
    {
        var sensors = GiosJsonParser.ParseSensors("""
        {"Lista stanowisk pomiarowych dla podanej stacji":[{"Identyfikator stanowiska":2747,"Wskaźnik - kod":"PM10"},{"Identyfikator stanowiska":2750,"Wskaźnik - kod":"PM2.5"}]}
        """);
        Assert.Equal(new[] { "PM10", "PM2.5" }, sensors.Select(s => s.Code).ToArray());

        var latest = GiosJsonParser.ParseLatest("""
        {"Lista danych pomiarowych":[{"Data":"2026-10-03 11:00:00","Wartość":null},{"Data":"2026-10-03 10:00:00","Wartość":23.9},{"Data":"2026-10-03 09:00:00","Wartość":20.1}]}
        """);
        Assert.NotNull(latest);
        Assert.Equal(23.9, latest.Value.Value);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.FromHours(2)), latest.Value.MeasuredAt);

        Assert.Equal("Dobry", GiosJsonParser.ParseIndexName("""{"AqIndex":{"Wartość indeksu":1,"Nazwa kategorii indeksu":"Dobry"}}"""));
    }

    [Fact]
    public void Imgw_hydro_keeps_stations_inside_area_and_reads_thresholds()
    {
        const string json = """
        [{"id_stacji":"1","stacja":"Ojców","rzeka":"Prądnik","lat":"50.20","lon":"19.83","stan_wody":"90","stan_ostrzegawczy":"150","stan_alarmowy":"180"},
         {"id_stacji":"2","stacja":"Gdańsk","rzeka":"Wisła","lat":"54.3","lon":"18.6","stan_wody":"500"}]
        """;

        var gauge = Assert.Single(ImgwJsonParser.ParseHydro(json, [], [], (lat, lon) => lat is >= 49.95 and <= 50.20 && lon is >= 19.70 and <= 20.30));
        Assert.Equal("1", gauge.Id);
        Assert.Equal(150, gauge.WarningLevelCm);
        Assert.Equal(180, gauge.AlarmLevelCm);
    }

    [Fact]
    public void Imgw_hydro_warnings_for_malopolska_apply_to_krakow()
    {
        const string json = """
        [{"numer":"12","biuro":"Kraków","stopień":"1","zdarzenie":"Wezbranie z przekroczeniem stanów ostrzegawczych","data_od":"2026-10-03 08:00:00","data_do":"9999-12-31 00:00:00","przebieg":"Wzrost stanów wody.","komentarz":"","obszary":[{"wojewodztwo":"małopolskie","opis":"zlewnia Raby"}]},
         {"numer":"13","biuro":"Gdynia","stopień":"2","zdarzenie":"X","obszary":[{"wojewodztwo":"pomorskie","opis":"Y"}]}]
        """;

        var w = Assert.Single(ImgwJsonParser.ParseHydroWarnings(json));
        Assert.True(w.AppliesTo("1261"));
        Assert.Equal(1, w.Level);
        Assert.Null(w.ValidTo);
        Assert.Contains("zlewnia Raby", w.Content);
    }

    [Fact]
    public void Open_data_page_reads_rows_formats_numbers_and_extracts_cursor()
    {
        const string json = """
        {"value":[{"Rok":2023.0,"Nazwa":"Żłobek nr 1","Miejsca":12.5,"Uwagi":null}],
         "nextLink":"https://srv-app-querona:8104/opendata-x/v1/t?$after=abc%3D%3D"}
        """;

        var page = OpenDataPortalParser.ParsePage(json);
        var row = Assert.Single(page.Rows);
        Assert.Equal("2023", row["Rok"]);
        Assert.Equal("12.5", row["Miejsca"]);
        Assert.Null(row["Uwagi"]);
        Assert.Equal("abc%3D%3D", page.NextAfter);
        Assert.Equal(new[] { "Rok", "Nazwa", "Miejsca", "Uwagi" }, OpenDataPortalParser.Columns(page.Rows).ToArray());
        Assert.Null(OpenDataPortalParser.ParsePage("""{"value":[]}""").NextAfter);
    }

    [Fact]
    public void Nfz_queue_page_maps_statistics_flags_and_next_link()
    {
        const string json = """
        {"meta":{"count":30},"links":{"next":"/app-itl-api/queues?page=2&limit=25"},
         "data":[{"type":"queues","id":"abc","attributes":{"case":1,"benefit":"PORADNIA ORTOPEDYCZNA","provider":"SZPITAL X","place":"Poradnia","address":"UL. Y 1","locality":"KRAKÓW","phone":"12 000","toilet":"Y","ramp":"N","car-park":"N","elevator":"Y","latitude":null,"longitude":null,
           "statistics":{"provider-data":{"awaiting":120,"removed":10,"average-period":45,"update":"2026-08"}},"dates":null}}]}
        """;

        var page = NfzJsonParser.ParseQueues(json);
        var e = Assert.Single(page.Entries);
        Assert.Equal(120, e.PeopleWaiting);
        Assert.Equal(45, e.AverageWaitDays);
        Assert.True(e.StepFree);
        Assert.False(e.Urgent);
        Assert.Null(e.Location);
        Assert.Equal("/app-itl-api/queues?page=2&limit=25", page.Next);
    }
}
