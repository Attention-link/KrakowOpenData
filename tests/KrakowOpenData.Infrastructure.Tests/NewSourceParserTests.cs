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

public class LiveReplacementTests
{
    [Fact]
    public void Overpass_response_splits_named_park_and_ride_from_amenities()
    {
        const string json = """
        {"elements":[
          {"type":"way","id":483476337,"center":{"lat":50.013,"lon":19.948},"tags":{"amenity":"parking","park_ride":"yes","name":"P+R Kurdwanów","capacity":"167","capacity:charging":"4","opening_hours":"Mo-Su 04:30-02:30","fee":"yes","addr:street":"Porucznika Jerzego Halszki","addr:housenumber":"1","addr:city":"Kraków"}},
          {"type":"way","id":1,"center":{"lat":50.01,"lon":20.03},"tags":{"amenity":"parking","park_ride":"yes"}},
          {"type":"node","id":2,"lat":50.06,"lon":19.93,"tags":{"emergency":"defibrillator","indoor":"yes","opening_hours":"24/7"}},
          {"type":"node","id":3,"lat":50.06,"lon":19.94,"tags":{"amenity":"drinking_water"}},
          {"type":"node","id":4,"lat":50.06,"lon":19.95,"tags":{"amenity":"bench"}}]}
        """;

        var snapshot = KrakowOpenData.Infrastructure.OpenStreetMap.OverpassParser.Parse(json);

        var pr = Assert.Single(snapshot.ParkAndRide); // the unnamed one is skipped
        Assert.Equal("P+R Kurdwanów", pr.Name);
        Assert.Equal(167, pr.Capacity);
        Assert.Equal(4, pr.EvChargers);
        Assert.Equal("Porucznika Jerzego Halszki 1, Kraków", pr.Address);
        Assert.Equal(new[] { KrakowOpenData.Domain.UrbanSpace.AmenityKind.Defibrillator, KrakowOpenData.Domain.UrbanSpace.AmenityKind.DrinkingWater },
            snapshot.Amenities.Select(a => a.Kind).ToArray());
        Assert.Contains("opening_hours: 24/7", snapshot.Amenities[0].Details);
    }

    [Fact]
    public void Districts_are_built_from_residents_table()
    {
        var table = new KrakowOpenData.Application.Abstractions.OpenDataTableContent(
            ["Nr_Dzielnicy", "Dzielnica", "Płeć", "Liczba_osób"],
            [
                Row(("Nr_Dzielnicy", "1"), ("Dzielnica", "Dzielnica I Stare Miasto"), ("Płeć", "K"), ("Liczba_osób", "14487")),
                Row(("Nr_Dzielnicy", "1"), ("Dzielnica", "Dzielnica I Stare Miasto"), ("Płeć", "M"), ("Liczba_osób", "12336")),
                Row(("Nr_Dzielnicy", "14"), ("Dzielnica", "Dzielnica XIV Czyżyny"), ("Płeć", "K"), ("Liczba_osób", "18430"))
            ],
            false, "test");

        var districts = CityTableMapper.Districts(table, "31 Dec 2025");

        Assert.Equal(2, districts.Count);
        Assert.Equal("I", districts[0].Id);
        Assert.Equal("Stare Miasto", districts[0].Name);
        Assert.Equal(26823, districts[0].RegisteredPopulation);
        Assert.Equal("XIV", districts[1].Id);
        Assert.Equal("Czyżyny", districts[1].Name);
    }

    [Fact]
    public void Service_cards_are_built_from_procedures_table()
    {
        var table = new KrakowOpenData.Application.Abstractions.OpenDataTableContent(
            ["Symbol usługi", "Nazwa usługi", "Adres do karty na BIP"],
            [Row(("Symbol usługi", "AM-13"), ("Nazwa usługi", "Opiniowanie wniosków dotyczących murali"), ("Adres do karty na BIP", "https://www.bip.krakow.pl/uslugi/AM-13"), ("Wersja Karty Uslugi", "8"))],
            false, "test");

        var card = Assert.Single(CityTableMapper.ServiceCards(table));

        Assert.Equal("AM-13", card.Id);
        Assert.Equal("AM", card.Topic);
        Assert.Equal("https://www.bip.krakow.pl/uslugi/AM-13", card.SourceUrl);
        Assert.True(card.RelevanceFor("murale") > 0 || card.RelevanceFor("murali") > 0);
    }

    private static IReadOnlyDictionary<string, string?> Row(params (string Key, string? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value);
}

public class StreetLightParserTests
{
    [Fact]
    public void Street_lamps_are_parsed_with_optional_details()
    {
        const string json = """
        {"elements":[
          {"type":"node","id":1,"lat":50.06,"lon":19.93,"tags":{"highway":"street_lamp","lamp_type":"led","lamp_mount":"bent_mast","height":"8 m","light:count":"2","operator":"Tauron","ref":"A12"}},
          {"type":"node","id":2,"lat":50.07,"lon":19.94,"tags":{"highway":"street_lamp","lamp_type":"electric","support":"pole"}},
          {"type":"node","id":3,"tags":{"highway":"street_lamp"}}]}
        """;

        var lights = KrakowOpenData.Infrastructure.OpenStreetMap.StreetLightParser.Parse(json);

        Assert.Equal(2, lights.Count); // the one without coordinates is skipped
        var first = lights[0];
        Assert.Equal("osm-n1", first.Id);
        Assert.Equal(KrakowOpenData.Domain.UrbanSpace.StreetLightTechnology.Led, first.Technology);
        Assert.Equal("bent_mast", first.Mount);
        Assert.Equal(8, first.HeightMeters);
        Assert.Equal(2, first.LightCount);
        Assert.Equal("Tauron", first.Operator);
        Assert.Equal("A12", first.Reference);
        Assert.Equal(KrakowOpenData.Domain.UrbanSpace.StreetLightTechnology.Unknown, lights[1].Technology);
        Assert.Equal("pole", lights[1].Mount);
    }

    [Fact]
    public void Query_asks_only_for_street_lamp_nodes_in_the_area()
    {
        var query = KrakowOpenData.Infrastructure.OpenStreetMap.StreetLightParser.BuildQuery("Kraków");
        Assert.Contains("node[\"highway\"=\"street_lamp\"](area.a)", query);
        Assert.Contains("\"name\"=\"Kraków\"", query);
    }
}
