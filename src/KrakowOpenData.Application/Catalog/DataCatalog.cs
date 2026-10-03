using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Catalog;

/// <summary>How a dataset reaches this solution.</summary>
public enum AccessMode
{
    /// <summary>Polled live feed (seconds to minutes).</summary>
    LiveFeed,

    /// <summary>Public JSON/REST API.</summary>
    Api,

    /// <summary>File downloaded and parsed periodically (e.g. GTFS zip).</summary>
    Download,

    /// <summary>Bundled seed data, curated by hand from official pages.</summary>
    Seed,

    /// <summary>Clearly-labelled sample data for demos; replace before relying on it.</summary>
    Sample,

    /// <summary>Known source, not wired up yet: the next thing to implement.</summary>
    Planned
}

public sealed record DatasetDescriptor(
    string Key,
    string Title,
    DataCategory Category,
    string Publisher,
    string SourceUrl,
    string Format,
    AccessMode Access,
    string? ApiRoute,
    string Notes);

/// <summary>
/// The list of every public dataset this solution knows about, grouped by category.
/// Add a row here when you add a new source; the API and UI read from this list.
/// </summary>
public static class DataCatalog
{
    public static IReadOnlyList<DatasetDescriptor> Datasets { get; } =
    [
        // ── Mobility ───────────────────────────────────────────────────────────
        new("transit-stops", "Public transport stops", DataCategory.Mobility, "ZTP Kraków",
            "https://gtfs.ztp.krakow.pl/", "GTFS (zip)", AccessMode.Download, "/api/mobility/stops",
            "Buses (A), trams (T) and agglomeration (M) feeds merged; ids prefixed with the feed key."),
        new("transit-routes", "Lines and routes", DataCategory.Mobility, "ZTP Kraków",
            "https://gtfs.ztp.krakow.pl/", "GTFS (zip)", AccessMode.Download, "/api/mobility/routes",
            "Route short names, colours and modes."),
        new("transit-departures", "Departures per stop (timetable + live delay)", DataCategory.Mobility, "ZTP Kraków",
            "https://gtfs.ztp.krakow.pl/", "GTFS + GTFS-RT", AccessMode.LiveFeed, "/api/mobility/stops/{stopId}/departures",
            "Combines stop_times with TripUpdates."),
        new("vehicle-positions", "Live vehicle positions", DataCategory.Mobility, "ZTP Kraków",
            "https://gtfs.ztp.krakow.pl/", "GTFS-RT (protobuf)", AccessMode.LiveFeed, "/api/mobility/vehicles",
            "VehiclePositions_A/T/M.pb, refreshed every few seconds upstream."),
        new("trip-updates", "Live trip delays", DataCategory.Mobility, "ZTP Kraków",
            "https://gtfs.ztp.krakow.pl/", "GTFS-RT (protobuf)", AccessMode.LiveFeed, "/api/mobility/trip-updates",
            "TripUpdates_A/T/M.pb."),
        new("service-alerts", "Disruptions and detours", DataCategory.Mobility, "ZTP Kraków",
            "https://gtfs.ztp.krakow.pl/", "GTFS-RT (protobuf)", AccessMode.LiveFeed, "/api/mobility/alerts",
            "ServiceAlerts_A/T/M.pb."),
        new("park-and-ride", "P+R car parks", DataCategory.Mobility, "ZTP Kraków / krakow.pl",
            "https://ztp.krakow.pl/en/park-and-ride/pr-car-parks", "Seed (JSON)", AccessMode.Seed, "/api/mobility/park-and-ride",
            "Names and known facts only; no public occupancy feed found."),
        new("city-bikes", "City bikes (LajkBike, Park-e-Bike)", DataCategory.Mobility, "City of Kraków",
            "https://www.krakow.pl", "Unknown", AccessMode.Planned, null,
            "No public GBFS feed found yet."),

        // ── Environment ────────────────────────────────────────────────────────
        new("weather-observations", "Weather observations", DataCategory.Environment, "IMGW-PIB",
            "https://danepubliczne.imgw.pl", "JSON API", AccessMode.Api, "/api/environment/weather",
            "Synoptic stations; Kraków station by default."),
        new("air-quality", "Air quality", DataCategory.Environment, "GIOŚ",
            "https://powietrze.gios.gov.pl/pjp/content/api", "JSON API", AccessMode.Api, "/api/environment/air-quality",
            "Latest hourly PM2.5, PM10 and NO₂ for every GIOŚ station in Kraków, plus the official Polish air quality index. No key needed."),

        // ── Climate & crisis ───────────────────────────────────────────────────
        new("river-gauges", "River and stream levels", DataCategory.ClimateAndCrisis, "IMGW-PIB / City of Kraków",
            "https://danepubliczne.imgw.pl", "JSON API + seed", AccessMode.Api, "/api/crisis/river-gauges",
            "Live IMGW stations near Kraków plus the city's local stream readings from 15 Sep 2024 for replay."),
        new("weather-warnings", "Weather and hydrological warnings", DataCategory.ClimateAndCrisis, "IMGW-PIB",
            "https://danepubliczne.imgw.pl", "JSON API", AccessMode.Api, "/api/crisis/warnings",
            "Filtered to Kraków county (TERYT 1261) by default."),
        new("flood-zones", "Flood hazard zones", DataCategory.ClimateAndCrisis, "MSIP Kraków / ISOK",
            "https://bip.krakow.pl/?dok_id=772", "WFS / SHP", AccessMode.Planned, null,
            "Download layers from MSIP and load as GeoJSON."),

        // ── Urban space ────────────────────────────────────────────────────────
        new("districts", "Districts (dzielnice)", DataCategory.UrbanSpace, "BIP Kraków",
            "https://www.bip.krakow.pl/?mmi=97", "Seed (JSON)", AccessMode.Seed, "/api/urban/districts",
            "All 18 districts; registered population where published."),
        new("msip-spatial", "Spatial data: parcels, buildings, green areas", DataCategory.UrbanSpace, "MSIP Kraków",
            "https://msip.krakow.pl", "WFS / SHP / GeoJSON", AccessMode.Planned, null,
            "District boundaries, addresses, streets, zoning plans. The WFS/REST endpoints returned 404 when checked (Oct 2026)."),
        new("osm-amenities", "Amenities from OpenStreetMap", DataCategory.UrbanSpace, "OpenStreetMap contributors",
            "https://overpass-api.de", "Overpass JSON", AccessMode.Planned, null,
            "Defibrillators (AED), drinking water, public toilets, EV chargers, bike parking. Overpass rate-limits heavy queries; not wired yet."),

        // ── Public services ────────────────────────────────────────────────────
        new("service-cards", "City service cards (procedures)", DataCategory.PublicServices, "BIP Kraków",
            "https://bip.krakow.pl/uslugi/GD-35", "Seed (JSON)", AccessMode.Seed, "/api/services/cards",
            "Copied by hand from BIP (it disallows crawling). Add more cards to the seed file."),
        new("nfz-waiting-lists", "NFZ treatment waiting lists", DataCategory.PublicServices, "NFZ",
            "https://api.nfz.gov.pl/app-itl-api/", "JSON API", AccessMode.Api, "/api/services/waiting-lists?benefit=ortoped",
            "Kraków providers for a searched service: people waiting and average wait in days. First-available dates are empty in the API."),

        // ── Society ────────────────────────────────────────────────────────────
        new("open-data-portal", "City Open Data portal tables", DataCategory.Society, "Urząd Miasta Krakowa",
            "https://otwartedane.um.krakow.pl/", "JSON API", AccessMode.Api, "/api/open-data/tables",
            "44 tables from api.um.krakow.pl: residents, labour market, tourism, culture, sport events, schools, nurseries, health, parks, vehicles, lost property.")
    ];

    public static IEnumerable<IGrouping<DataCategory, DatasetDescriptor>> ByCategory() =>
        Datasets.GroupBy(d => d.Category).OrderBy(g => g.Key);

    public static DatasetDescriptor? Find(string key) =>
        Datasets.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string DescribeCategory(DataCategory category) => category switch
    {
        DataCategory.Mobility => "Public transport, park and ride, bikes",
        DataCategory.Environment => "Weather and air quality",
        DataCategory.ClimateAndCrisis => "River levels, warnings, flood risk",
        DataCategory.UrbanSpace => "Districts, land use, spatial data",
        DataCategory.PublicServices => "City procedures, offices, schools, nurseries, healthcare",
        DataCategory.Society => "Residents, jobs, tourism, culture, sport",
        _ => category.ToString()
    };
}
