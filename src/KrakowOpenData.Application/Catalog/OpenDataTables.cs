using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Catalog;

/// <summary>
/// One table published by the City of Kraków Open Data API (api.um.krakow.pl).
/// Rows are read as-is, so the column names are the city's own (in Polish).
/// </summary>
public sealed record OpenDataTable(string Key, string Title, DataCategory Category, string Api, string Table)
{
    public const string ApiBaseUrl = "https://api.um.krakow.pl/";
    public const string PortalUrl = "https://otwartedane.um.krakow.pl/";

    /// <summary>Public JSON endpoint, e.g. https://api.um.krakow.pl/opendata-kluby/v1/Kluby.</summary>
    public string ApiUrl => $"{ApiBaseUrl}{Api}/v1/{Table}";
}

/// <summary>
/// Every table from the city's Open Data API that this solution exposes, checked on 3 Oct 2026.
/// The API needs no key and returns 100 rows per page. Where the city publishes one table per
/// year, the latest year is listed; add older years the same way.
/// </summary>
public static class OpenDataTables
{
    public static IReadOnlyList<OpenDataTable> All { get; } =
    [
        // ── Residents and labour market ────────────────────────────────────────
        new("residents-statistics", "Residents: statistics", DataCategory.Society, "opendata-mieszkancy-statystyki", "Statystyka_mieszkancy"),
        new("permanent-residents", "Residents registered for permanent stay (31 Dec 2025)", DataCategory.Society, "opendata-mieszkancy-pobyt-staly", "zameldowani-na-pobyt-staly-31-12-2025"),
        new("permanent-residents-2024", "Residents registered for permanent stay (31 Dec 2024)", DataCategory.Society, "opendata-mieszkancy-pobyt-staly", "zameldowani-na-pobyt-staly-31-12-2024"),
        new("unemployed", "Registered unemployed", DataCategory.Society, "opendata-przedsiebiorczosc-i-nauka", "bezrobotni"),
        new("unemployment-flows", "Unemployment inflow and outflow", DataCategory.Society, "opendata-przedsiebiorczosc-i-nauka", "naplyw-odplyw"),
        new("job-offers", "Job offers", DataCategory.Society, "opendata-przedsiebiorczosc-i-nauka", "oferty-pracy"),
        new("work-declarations", "Declarations on entrusting work to foreigners", DataCategory.Society, "opendata-przedsiebiorczosc-i-nauka", "oswiadczenia"),

        // ── Tourism ────────────────────────────────────────────────────────────
        new("visitors-total", "Visitors in total", DataCategory.Society, "opendata-turystyka-i-promocja-ruch-turystyczny", "ruch-turystyczny-odwiedzajacy-ogolem"),
        new("visitors-domestic", "Domestic visitors by voivodeship", DataCategory.Society, "opendata-turystyka-i-promocja-ruch-turystyczny", "ruch-turystyczny-odwiedzajacy-wg-wojewodztw-krajowi"),
        new("visitors-foreign", "Foreign visitors, top 30 countries", DataCategory.Society, "opendata-turystyka-i-promocja-ruch-turystyczny", "ruch-turystyczny-odwiedzajacy-wg-krajow-zagraniczni-top30"),
        new("day-visitors", "Day visitors", DataCategory.Society, "opendata-turystyka-i-promocja-ruch-turystyczny", "ruch-turystyczny-odwiedzajacy-jednodniowi"),
        new("tourists", "Tourists (overnight stays)", DataCategory.Society, "opendata-turystyka-i-promocja-ruch-turystyczny", "ruch-turystyczny-turysci"),
        new("stay-length", "Length of stay", DataCategory.Society, "opendata-turystyka-i-promocja-ruch-turystyczny", "ruch-turystyczny-dlugosc-pobytu-ogolem"),
        new("business-meetings", "Business meeting participants (2025)", DataCategory.Society, "opendata-turystyka-i-promocja-spotkania-biznesowe", "liczba-uczestnikow-spotkan-biznesowych-2025"),

        // ── Culture ────────────────────────────────────────────────────────────
        new("libraries", "Kraków Library branches", DataCategory.Society, "opendata-kultura-lista-bibliotek", "biblioteka-krakow-filie"),
        new("library-activity", "Library activity since 2021", DataCategory.Society, "opendata-kultura-dzialalnosc-biblioteki", "biblioteka-dzialalnosc-od-2021"),
        new("museums", "City museums", DataCategory.Society, "opendata-kultura-lista-muzeow", "muzea-miejskie"),
        new("theatres", "City theatres", DataCategory.Society, "opendata-kultura-lista-teatrow", "teatry-miejskie"),
        new("theatre-performances", "Theatre performances (2025)", DataCategory.Society, "opendata-kultura-dzialalnosc-teatrow", "dzialalnosc-teatrow-teatry-przedstawienia-2025"),
        new("orchestras", "Music institutions and orchestras (2025)", DataCategory.Society, "opendata-kultura-dzialalnosc-instytucji-muzycznych", "dzialalnosc-instutucji-muzycznych-orkiestry-2025"),
        new("culture-nights", "Kraków Nights events", DataCategory.Society, "opendata-kultura-krakowskie-noce-sztuka-do-rzeczy", "Krakowskie_Noce_Sztuka_do_rzeczy"),

        // ── Sport events ───────────────────────────────────────────────────────
        new("night-run", "Night Run: participants and results", DataCategory.Society, "opendata-sport-bieg-nocny", "Bieg_Nocny"),
        new("half-marathon", "Cracovia Half Marathon: participants and results", DataCategory.Society, "opendata-sport-cracovia-polmaraton", "Cracovia_Polmaraton"),
        new("marathon", "Cracovia Marathon: participants and results", DataCategory.Society, "opendata-sport-cracovia-maraton", "Cracovia_Maraton"),
        new("krakow-five", "Kraków 5K: participants and results", DataCategory.Society, "opendata-sport-krakowska-piatka", "Krakowska_Piatka"),
        new("womens-run", "Kraków Women's Run: participants and results", DataCategory.Society, "opendata-sport-krakowski-bieg-kobiet", "Krakowski_Bieg_Kobiet"),
        new("three-mounds-run", "Three Mounds Run: participants and results", DataCategory.Society, "opendata-bieg-trzech-kopcow-uczestnicy-i-wyniki", "bieg-trzech-kopcow"),

        // ── Education, childcare and health ────────────────────────────────────
        new("kindergartens-public", "Children in public kindergartens (2025/26)", DataCategory.PublicServices, "opendata-oswiata-przedszkola-liczba-dzieci", "liczba-dzieci-przedszkola-samorzadowe-2025-2026"),
        new("kindergartens-private", "Children in non-public kindergartens (2025/26)", DataCategory.PublicServices, "opendata-oswiata-przedszkola-liczba-dzieci", "liczba-dzieci-przedszkola-niesamorzadowe-2025-2026"),
        new("primary-schools-public", "Pupils in public primary schools (2025/26)", DataCategory.PublicServices, "opendata-oswiata-szkoly-podstawowe-liczba-uczniow", "uczniowie-szkoly-podstawowe-samorzadowe-2025-2026"),
        new("primary-schools-private", "Pupils in non-public primary schools (2025/26)", DataCategory.PublicServices, "opendata-oswiata-szkoly-podstawowe-liczba-uczniow", "uczniowie-szkoly-podstawowe-niesamorzadowe-2025-2026"),
        new("music-schools", "Pupils in public music schools (2025/26)", DataCategory.PublicServices, "opendata-oswiata-szkoly-muzyczne-gmk", "uczniowie-szkoly-muzyczne-samorzadowe-2025-2026"),
        new("nurseries-public", "Public nurseries (żłobki)", DataCategory.PublicServices, "opendata-zdrowie-zlobki-samorzadowe", "zlobki-samorzadowe"),
        new("nurseries-private", "Private nurseries", DataCategory.PublicServices, "opendata-zdrowie-zlobki-prywatne", "zlobki-prywatne"),
        new("nurseries-commissioned", "Nursery places commissioned by the city", DataCategory.PublicServices, "opendata-zdrowie-zlobki-gmk", "zlobki-zlecenie-gmk"),
        new("childrens-clubs", "Children's clubs", DataCategory.PublicServices, "opendata-zdrowie-kluby-dzieciece", "kluby-dzieciece"),
        new("healthcare-providers", "City-owned healthcare providers", DataCategory.PublicServices, "opendata-zdrowie-podmioty-lecznicze-gmk", "Podmioty_lecznicze_GMK"),
        new("senior-centres", "Senior activity centres", DataCategory.PublicServices, "opendata-spoleczenstwo-centra-aktywnosci-seniora", "cas-lista"),
        new("clubs", "Clubs (Kluby)", DataCategory.PublicServices, "opendata-kluby", "Kluby"),
        new("city-procedures", "City services and procedures (BIP)", DataCategory.PublicServices, "opendata-administracja-lista-uslug-i-procedur", "uslugi-adm-procedury-zew-gmk"),
        new("lost-property", "Lost property office: found items", DataCategory.PublicServices, "opendata-biuro-rzeczy-znalezionych", "Wykaz_rzeczy_znalezionych_GMK"),

        // ── Environment and mobility ───────────────────────────────────────────
        new("city-parks", "City parks: area", DataCategory.Environment, "opendata-srodowisko-parki-miejskie", "parki-miejskie-powierzchnia"),
        new("pocket-parks", "Pocket parks: area", DataCategory.Environment, "opendata-srodowisko-parki-kieszonkowe", "parki-kieszonkowe-powierzchnia"),
        new("registered-vehicles", "Registered vehicles", DataCategory.Mobility, "opendata-transport-zarejestrowane-pojazdy", "zarejstrowane-pojazdy")
    ];

    public static OpenDataTable? Find(string key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}
