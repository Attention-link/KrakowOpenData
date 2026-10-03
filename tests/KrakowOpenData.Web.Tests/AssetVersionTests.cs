using KrakowOpenData.Web.Assets;
using Microsoft.Extensions.FileProviders;

namespace KrakowOpenData.Web.Tests;

/// <summary>
/// Cloudflare caches the site's css/js for hours. Each build must give them new URLs, or visitors keep the previous version.
/// </summary>
public class AssetVersionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string UrlOf(string path)
    {
        using var files = new PhysicalFileProvider(_root);
        return AssetVersions.Versioned(files, path);
    }

    [Fact]
    public void A_changed_file_gets_a_new_url()
    {
        File.WriteAllText(Path.Combine(_root, "app.css"), "body { color: red; }");
        var before = UrlOf("app.css");
        File.WriteAllText(Path.Combine(_root, "app.css"), "body { color: blue; }");

        Assert.StartsWith("app.css?v=", before);
        Assert.NotEqual(before, UrlOf("app.css"));
    }

    [Fact]
    public void An_unchanged_file_keeps_its_url()
    {
        File.WriteAllText(Path.Combine(_root, "app.css"), "body { color: red; }");
        Assert.Equal(UrlOf("app.css"), UrlOf("app.css"));
    }

    [Fact]
    public void A_missing_file_is_left_as_it_is() => Assert.Equal("js/missing.js", UrlOf("js/missing.js"));

    [Fact]
    public void Every_local_stylesheet_and_script_in_the_page_shell_is_versioned()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "KrakowOpenData.Web", "Components", "App.razor"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var app = File.ReadAllText(Path.Combine(dir!.FullName, "src", "KrakowOpenData.Web", "Components", "App.razor"));

        var tags = System.Text.RegularExpressions.Regex.Matches(app, "<(link rel=\"stylesheet\"|script)[^>]*>").Select(m => m.Value).ToList();
        Assert.NotEmpty(tags);
        // blazor.web.js comes from the framework, not wwwroot, and changes only with the .NET version.
        foreach (var tag in tags.Where(t => !t.Contains("_framework/")))
            Assert.Contains("AssetUrls.Url(", tag);
    }
}
