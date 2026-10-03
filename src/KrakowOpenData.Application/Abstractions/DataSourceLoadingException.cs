namespace KrakowOpenData.Application.Abstractions;

/// <summary>
/// A dataset is still being downloaded for the first time. The API answers 503 with a Retry-After hint
/// instead of making the caller wait for a slow upstream source.
/// </summary>
public sealed class DataSourceLoadingException(string dataset, TimeSpan retryAfter)
    : Exception($"{dataset} is still downloading from its public source. Try again in about {Math.Ceiling(retryAfter.TotalSeconds)} seconds.")
{
    public string Dataset { get; } = dataset;

    public TimeSpan RetryAfter { get; } = retryAfter;
}
