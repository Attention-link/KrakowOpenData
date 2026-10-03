namespace KrakowOpenData.Client;

/// <summary>Settings for <see cref="ServiceCollectionExtensions.AddKrakowOpenDataClient(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{KrakowOpenDataClientOptions})"/>.</summary>
public sealed class KrakowOpenDataClientOptions
{
    /// <summary>Where KrakowOpenData.Api runs, e.g. http://localhost:5080/.</summary>
    public Uri BaseAddress { get; set; } = new("http://localhost:5080/");

    /// <summary>The first live request can take a minute while the API downloads timetables.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Retries for GET requests that fail with 502/503/504 or a network error. 0 disables.</summary>
    public int MaxRetries { get; set; } = 2;
}
