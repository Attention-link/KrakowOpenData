using System.Net;
using KrakowOpenData.Client.Http;

namespace KrakowOpenData.Api.Tests;

public class RetryUpstreamHandlerTests
{
    [Fact]
    public async Task Get_is_retried_on_503_then_succeeds()
    {
        var inner = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(new RetryUpstreamHandler(2) { InnerHandler = inner });

        var response = await http.GetAsync("http://test/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var inner = new SequenceHandler(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        using var http = new HttpClient(new RetryUpstreamHandler(2) { InnerHandler = inner });

        var response = await http.GetAsync("http://test/api");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statuses[Math.Min(Calls++, statuses.Length - 1)]));
    }
}
