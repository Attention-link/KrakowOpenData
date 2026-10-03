using System.Text.RegularExpressions;

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

    [Fact]
    public void The_menu_links_to_the_resident_app_and_the_planner_dashboard()
    {
        var links = LinksIntoTheSafetyApp();
        Assert.Contains(links, l => l.Contains("href=\"/safety/index.html\""));
        Assert.Contains(links, l => l.Contains("href=\"/safety/index.html#/planner\""));
    }

    [Fact]
    public void Every_link_into_the_safety_app_is_loaded_by_the_browser_not_by_the_blazor_router()
    {
        foreach (var link in LinksIntoTheSafetyApp())
            Assert.Contains("target=\"_top\"", link);
    }
}
