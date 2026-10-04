namespace KrakowOpenData.Infrastructure.Options;

/// <summary>Configuration section "KrakowData" in appsettings.json.</summary>
public sealed class KrakowDataOptions
{
    public const string SectionName = "KrakowData";

    /// <summary>GTFS feeds to merge. ZTP Kraków publishes A (bus), T (tram) and M (agglomeration).</summary>
    public List<GtfsFeedOptions> GtfsFeeds { get; set; } = [];

    public string ImgwBaseUrl { get; set; } = "https://danepubliczne.imgw.pl/api/data/";

    /// <summary>Synoptic station name used in /synop/station/{name}.</summary>
    public string ImgwSynopStation { get; set; } = "krakow";

    /// <summary>Hydro stations whose name contains any of these (case-insensitive) are kept.</summary>
    public List<string> ImgwHydroStationNameFilters { get; set; } = ["KRAK"];

    /// <summary>Explicit IMGW hydro station ids to keep in addition to the name filter.</summary>
    public List<string> ImgwHydroStationIds { get; set; } = [];

    /// <summary>
    /// Hydro stations inside this box are kept too. The default covers Kraków and the streams
    /// flowing into it (Wisła, Rudawa, Prądnik, Wilga).
    /// </summary>
    public GeoBoxOptions ImgwHydroArea { get; set; } = new();

    /// <summary>GIOŚ air quality API (no key).</summary>
    public string GiosBaseUrl { get; set; } = "https://api.gios.gov.pl/pjp-api/v1/rest/";

    /// <summary>Stations whose "Nazwa miasta" equals this are used.</summary>
    public string GiosCity { get; set; } = "Kraków";

    /// <summary>City of Kraków Open Data API (no key).</summary>
    public string OpenDataApiBaseUrl { get; set; } = "https://api.um.krakow.pl/";

    /// <summary>NFZ waiting lists API (no key).</summary>
    public string NfzBaseUrl { get; set; } = "https://api.nfz.gov.pl/app-itl-api/";

    /// <summary>NFZ province code; 06 = małopolskie.</summary>
    public string NfzProvince { get; set; } = "06";

    public string NfzLocality { get; set; } = "KRAKÓW";

    /// <summary>NFZ pages (25 rows each) to read per search.</summary>
    public int NfzMaxPages { get; set; } = 4;

    public int AirQualityRefreshMinutes { get; set; } = 20;

    /// <summary>Photon geocoder (OpenStreetMap address search with autocomplete) behind the address boxes of the Kompas Krakowa app.</summary>
    public string GeocoderBaseUrl { get; set; } = "https://photon.komoot.io/";

    /// <summary>
    /// OpenStreetMap Overpass endpoints, tried in order: if one is slow, busy (429/504) or down,
    /// the next mirror is used. All serve the same data.
    /// </summary>
    public List<string> OverpassUrls { get; set; } =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.private.coffee/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
        "https://overpass.openstreetmap.fr/api/interpreter"
    ];

    /// <summary>Per-request timeout for Overpass; the citywide street-lamp query is several MB.</summary>
    public int OverpassTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Download OpenStreetMap datasets in the background when the API starts (and refresh them daily),
    /// so requests never wait for Overpass. Tests switch this off.
    /// </summary>
    public bool PreloadOnStartup { get; set; } = true;

    /// <summary>How long a request waits for a dataset's very first download before answering 503.</summary>
    public int FirstLoadWaitSeconds { get; set; } = 20;

    /// <summary>
    /// Where the last successful OpenStreetMap downloads are saved, so a restart serves them at once.
    /// Empty = %LOCALAPPDATA%/KrakowOpenData/cache (or ~/.local/share/... on Linux).
    /// </summary>
    public string CacheDirectory { get; set; } = string.Empty;

    /// <summary>OSM boundary name (admin_level 8) to search in.</summary>
    public string OsmAreaName { get; set; } = "Kraków";

    /// <summary>Overpass is queried at most this often (one combined query).</summary>
    public int OsmRefreshMinutes { get; set; } = 1440;

    public int OpenDataRefreshMinutes { get; set; } = 360;

    /// <summary>
    /// Area for the accessibility data (steps, kerbs, lifts, entrances, toilets, benches, path surfaces). The full city with path
    /// geometry is heavy for Overpass, so it is a box; the default is central Kraków (19.90–19.99 E, 50.035–50.08 N).
    /// </summary>
    public GeoBoxOptions AccessArea { get; set; } = new() { MinLatitude = 50.035, MaxLatitude = 50.08, MinLongitude = 19.90, MaxLongitude = 19.99 };

    public int StaticRefreshMinutes { get; set; } = 360;

    public int RealtimeRefreshSeconds { get; set; } = 20;

    public int ApiRefreshMinutes { get; set; } = 10;

    public int HttpTimeoutSeconds { get; set; } = 60;
}

public sealed class GtfsFeedOptions
{
    /// <summary>Short key used to prefix ids, e.g. "T" makes stop "123" become "T:123".</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string StaticUrl { get; set; } = string.Empty;

    public string? VehiclePositionsUrl { get; set; }

    public string? TripUpdatesUrl { get; set; }

    public string? ServiceAlertsUrl { get; set; }
}

public sealed class GeoBoxOptions
{
    public double MinLatitude { get; set; } = 49.95;
    public double MaxLatitude { get; set; } = 50.20;
    public double MinLongitude { get; set; } = 19.70;
    public double MaxLongitude { get; set; } = 20.30;

    public bool Contains(double latitude, double longitude) =>
        latitude >= MinLatitude && latitude <= MaxLatitude && longitude >= MinLongitude && longitude <= MaxLongitude;
}
