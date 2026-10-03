using System.Reflection;

namespace KrakowOpenData.Infrastructure.Common;

/// <summary>
/// Snapshots of the OpenStreetMap downloads that ship inside the assembly (the <c>Seed</c> folder). They are what a machine that has
/// never downloaded the data (or cannot reach Overpass) starts from, so the app does not depend on what happens to be cached on
/// someone's computer. Their saved date is <see cref="TakenAt"/>, so the normal refresh replaces them as soon as the source answers.
/// </summary>
public static class SeedSnapshots
{
    /// <summary>
    /// The date the bundled snapshots are given. Deliberately old (they were taken in October 2026), so they always count as stale:
    /// the app serves them straight away and the normal refresh replaces them as soon as the source answers.
    /// </summary>
    public static readonly DateTimeOffset TakenAt = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Assembly Source = typeof(SeedSnapshots).Assembly;

    /// <summary>Names of the bundled snapshots (for example <c>osm-street-lights</c>).</summary>
    public static IEnumerable<string> Names => Source.GetManifestResourceNames()
        .Where(n => n.StartsWith("Seed.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
        .Select(n => n["Seed.".Length..^".json".Length]);

    public static async Task<(string Content, DateTimeOffset SavedAt)?> LoadAsync(string name, CancellationToken ct)
    {
        await using var stream = Source.GetManifestResourceStream($"Seed.{name}.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return (await reader.ReadToEndAsync(ct), TakenAt);
    }
}
