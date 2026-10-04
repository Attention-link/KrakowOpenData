using KrakowOpenData.Web.Localization;

namespace KrakowOpenData.Web.Navigation;

/// <summary>One menu entry. <see cref="LabelKey"/> is a <see cref="UiText"/> key; <see cref="Href"/> is always absolute.</summary>
public sealed record SiteMenuItem(string Href, string LabelKey, string? Icon = null, bool Staff = false)
{
    /// <summary>
    /// Pages of the Kompas Krakowa app (/safety/) are not Blazor routes: a link to them must load the page (target="_top"),
    /// or the Blazor router answers "Not found".
    /// </summary>
    public bool InApp => Href.StartsWith("/safety/", StringComparison.Ordinal);
}

public sealed record SiteMenuGroup(string? HeadingKey, IReadOnlyList<SiteMenuItem> Items, bool Features = false);

/// <summary>
/// The one menu of the whole site. The portal's side menu (NavMenu.razor) is rendered from it, and the Kompas Krakowa app
/// shows the same entries behind its "Menu" button (served as /safety/site-menu.json), so every page offers the same way around.
/// </summary>
public static class SiteMenu
{
    public static IReadOnlyList<SiteMenuGroup> Groups { get; } =
    [
        new(null,
        [
            new("/", "nav.home"),
            new("/catalog", "nav.catalog")
        ]),
        new("nav.safetyGroup",
        [
            new("/safety/index.html#/", "nav.safetyApp", "☀"),
            new("/safety/index.html#/access", "nav.access", "♿"),
            new("/safety/index.html#/notifications", "nav.telegram", "\U0001F514"),
            new("/safety/index.html#/planner", "nav.safetyPlanner", "\U0001F512", Staff: true)
        ], Features: true),
        new("cat.Mobility",
        [
            new("/mobility/stops", "nav.stops"),
            new("/mobility/vehicles", "nav.vehicles"),
            new("/mobility/alerts", "nav.alerts"),
            new("/mobility/park-and-ride", "nav.parkAndRide")
        ]),
        new("cat.Environment",
        [
            new("/environment/weather", "nav.weather"),
            new("/environment/air-quality", "nav.airQuality")
        ]),
        new("cat.ClimateAndCrisis",
        [
            new("/crisis/rivers", "nav.rivers"),
            new("/crisis/warnings", "nav.warnings")
        ]),
        new("cat.UrbanSpace",
        [
            new("/urban/districts", "nav.districts"),
            new("/urban/amenities", "nav.amenities"),
            new("/urban/street-lights", "nav.streetLights")
        ]),
        new("cat.PublicServices",
        [
            new("/services/cards", "nav.serviceCards"),
            new("/services/waiting-lists", "nav.waitingLists")
        ]),
        new("cat.Society",
        [
            new("/open-data", "nav.openData")
        ])
    ];

    /// <summary>Linked at the bottom of every page: the accessibility statement covers the whole service.</summary>
    public static IReadOnlyList<SiteMenuItem> Footer { get; } =
    [
        new("/safety/index.html#/accessibility", "footer.statement")
    ];

    public static IEnumerable<SiteMenuItem> AllItems => Groups.SelectMany(g => g.Items).Concat(Footer);

    /// <summary>The menu in every language, for the app: { "pl": { title, staff, groups: [{ heading, items }], footer }, ... }.</summary>
    public static IReadOnlyDictionary<string, object> ForApp() =>
        AppLanguages.All.ToDictionary(l => l.Code(), l =>
        {
            var t = Translator.For(l);
            object Item(SiteMenuItem i) => new { label = t[i.LabelKey], href = i.Href, icon = i.Icon, staff = i.Staff };
            return (object)new
            {
                title = t["nav.menu"],
                staff = t["nav.staff"],
                groups = Groups.Select(g => new { heading = g.HeadingKey is null ? null : t[g.HeadingKey], items = g.Items.Select(Item) }),
                footer = Footer.Select(Item)
            };
        });
}
