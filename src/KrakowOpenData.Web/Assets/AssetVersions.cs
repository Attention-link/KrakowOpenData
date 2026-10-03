using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;

namespace KrakowOpenData.Web.Assets;

/// <summary>
/// Adds a content hash to a wwwroot file's URL ("app.css" becomes "app.css?v=1a2b3c4d5e6f"), like asp-append-version.
/// The site sits behind Cloudflare, which keeps css/js for hours; a new build changes the URL, so it is never served stale.
/// </summary>
public sealed class AssetVersions(IWebHostEnvironment environment)
{
    private readonly ConcurrentDictionary<string, string> _urls = new();

    public string Url(string path) => _urls.GetOrAdd(path, p => Versioned(environment.WebRootFileProvider, p));

    public static string Versioned(IFileProvider files, string path)
    {
        var file = files.GetFileInfo(path.TrimStart('/'));
        if (!file.Exists) return path;
        using var stream = file.CreateReadStream();
        return $"{path}?v={Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant()}";
    }
}
