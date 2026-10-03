using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using KrakowOpenData.Infrastructure.DataSources;

namespace KrakowOpenData.Infrastructure.Seed;

/// <summary>
/// Loads curated JSON files embedded in this assembly (Seed/Data/*.json). These hold facts copied
/// by hand from official city pages, plus clearly-labelled SAMPLE files for offline demos.
/// </summary>
public static class SeedData
{
    public const string ParkAndRide = "park-and-ride.json";
    public const string Districts = "districts.json";
    public const string ServiceCards = "service-cards.json";
    public const string LocalRiverGauges = "local-river-gauges-2024-09-15.json";
    public const string SampleAirQuality = "sample-air-quality.json";
    public const string SampleWeather = "sample-weather.json";
    public const string SampleWarnings = "sample-warnings.json";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<string> AvailableFiles() =>
        typeof(SeedData).Assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static IReadOnlyList<T> Load<T>(string fileName)
    {
        var assembly = typeof(SeedData).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Embedded seed file '{fileName}' not found.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<List<T>>(stream, JsonOptions)
               ?? throw new InvalidDataException($"Seed file '{fileName}' is empty.");
    }

    public static IDataSource<T> Source<T>(string fileName)
    {
        var lazy = new Lazy<IReadOnlyList<T>>(() => Load<T>(fileName));
        return new DelegateDataSource<T>(_ => Task.FromResult(lazy.Value));
    }
}
