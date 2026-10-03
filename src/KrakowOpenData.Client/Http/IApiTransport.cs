namespace KrakowOpenData.Client.Http;

/// <summary>Sends GET requests and turns responses into objects or <see cref="KrakowApiException"/>.</summary>
internal interface IApiTransport
{
    Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct);

    /// <summary>Like <see cref="GetAsync{T}"/>, but 404 returns null.</summary>
    Task<T?> GetOrNullAsync<T>(string relativeUrl, CancellationToken ct) where T : class;
}
