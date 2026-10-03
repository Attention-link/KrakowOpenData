using System.Net;

namespace KrakowOpenData.Client.Http;

/// <summary>
/// Retries GET requests a few times with a short back-off when the API or a public source it relies
/// on is briefly unavailable (502/503/504 or a network error). Other requests pass through unchanged.
/// </summary>
public sealed class RetryUpstreamHandler(int maxRetries) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var lastAttempt = attempt >= maxRetries || request.Method != HttpMethod.Get;
            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (lastAttempt || !IsTransient(response.StatusCode)) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (!lastAttempt)
            {
                // Network error: retry below.
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}
