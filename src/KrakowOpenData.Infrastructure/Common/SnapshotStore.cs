using KrakowOpenData.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Infrastructure.Common;

/// <summary>Saves the raw text of a successful download so the next start can use it immediately.</summary>
public interface ISnapshotStore
{
    Task<(string Content, DateTimeOffset SavedAt)?> LoadAsync(string name, CancellationToken ct);

    Task SaveAsync(string name, string content, CancellationToken ct);
}

/// <summary>Snapshots as files in a cache folder. Failures are logged and ignored: the cache is optional.</summary>
public sealed class FileSnapshotStore(IOptions<KrakowDataOptions> options, ILogger<FileSnapshotStore> logger) : ISnapshotStore
{
    private readonly string _directory = string.IsNullOrWhiteSpace(options.Value.CacheDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KrakowOpenData", "cache")
        : options.Value.CacheDirectory;

    public async Task<(string Content, DateTimeOffset SavedAt)?> LoadAsync(string name, CancellationToken ct)
    {
        var path = PathFor(name);
        try
        {
            if (!File.Exists(path)) return null;
            return (await File.ReadAllTextAsync(path, ct), new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read cached {Name}", name);
            return null;
        }
    }

    public async Task SaveAsync(string name, string content, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var path = PathFor(name);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, content, ct);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save cached {Name}", name);
        }
    }

    private string PathFor(string name) => Path.Combine(_directory, name + ".json");
}
