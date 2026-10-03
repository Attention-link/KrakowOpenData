using System.Net;

namespace KrakowOpenData.Client;

/// <summary>
/// Thrown when the API answers with an error. <see cref="IsUpstreamUnavailable"/> (HTTP 503) means a
/// public data source (ZTP, IMGW, GIOŚ, NFZ, …) is down right now; retrying later usually helps.
/// </summary>
public sealed class KrakowApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public bool IsUpstreamUnavailable => StatusCode == HttpStatusCode.ServiceUnavailable;
}
