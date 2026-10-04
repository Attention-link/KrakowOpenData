using System.Text.RegularExpressions;
using KrakowOpenData.Web.Localization;
using KrakowOpenData.Web.Navigation;

namespace KrakowOpenData.Web.Tests;

/// <summary>
/// The safety app (Kompas Krakowa) lives at /safety/ and is not a Blazor page. A plain link to it is caught by the Blazor router,
/// which then shows "Not found" while the address bar says /safety/index.html#/planner. Links to it must open it as a new page
/// (target="_top"), so the browser loads it.
/// </summary>
public class SafetyLinkTests
{
    private static string ComponentsFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "KrakowOpenData.Web", "Components"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "KrakowOpenData.Web", "Components");
    }

    private static List<string> LinksIntoTheSafetyApp()
    {
        var links = new List<string>();
        foreach (var file in Directory.EnumerateFiles(ComponentsFolder(), "*.razor", SearchOption.AllDirectories))
            links.AddRange(Regex.Matches(File.ReadAllText(file), "<a\\b[^>]*href=\"/safety/[^\"]*\"[^>]*>", RegexOptions.IgnoreCase).Select(m => m.Value));
        return links;
    }

    [Theory]
    [InlineData("/safety/index.html#/")]                // the resident app
    [InlineData("/safety/index.html#/access")]          // Dostępność (Kraków bez barier)
    [InlineData("/safety/index.html#/notifications")]   // Telegram alerts
    [InlineData("/safety/index.html#/planner")]         // planner dashboard (staff)
    [InlineData("/safety/index.html#/accessibility")]   // accessibility statement, in the footer of every page
    public void The_site_menu_links_to_every_app_feature(string href) =>
        Assert.Contains(SiteMenu.AllItems, i => i.Href == href);

    [Fact]
    public void Every_portal_page_is_in_the_site_menu_with_a_label_in_every_language()
    {
        foreach (var item in SiteMenu.AllItems)
        {
            Assert.StartsWith("/", item.Href);
            foreach (var language in AppLanguages.All)
                Assert.True(Translator.For(language).Has(item.LabelKey), $"{item.LabelKey} has no {language} text");
        }
    }

    [Fact]
    public void The_app_gets_the_same_menu_in_every_language()
    {
        var menu = SiteMenu.ForApp();
        Assert.Equal(AppLanguages.All.Select(l => l.Code()).Order(), menu.Keys.Order());
        var json = System.Text.Json.JsonSerializer.Serialize(menu["pl"],
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.Contains("Dostępność – Kraków bez barier", json);
        Assert.Contains("/safety/index.html#/notifications", json);
    }

    [Fact]
    public void The_side_menu_is_rendered_from_the_site_menu_and_opens_app_links_as_pages()
    {
        var nav = File.ReadAllText(Path.Combine(ComponentsFolder(), "Layout", "NavMenu.razor"));
        Assert.Contains("SiteMenu.Groups", nav);
        Assert.Matches("<a [^>]*href=\"@item.Href\" target=\"_top\"", nav);
    }

    [Fact]
    public void Every_link_into_the_safety_app_is_loaded_by_the_browser_not_by_the_blazor_router()
    {
        foreach (var link in LinksIntoTheSafetyApp())
            Assert.Contains("target=\"_top\"", link);
    }
}
