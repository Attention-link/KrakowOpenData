namespace KrakowOpenData.Api.Endpoints;

/// <summary>
/// Turns failures of upstream public-data sources (network errors, timeouts, broken payloads)
/// into a 503 ProblemDetails instead of a generic 500, so clients can show "source unavailable".
/// </summary>
public sealed class UpstreamUnavailableFilter(ILogger<UpstreamUnavailableFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception ex) when (IsUpstreamFailure(ex) && !context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Upstream data source unavailable for {Path}", context.HttpContext.Request.Path);
            return Results.Problem(
                title: "Upstream data source unavailable",
                detail: ex.GetBaseException().Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static bool IsUpstreamFailure(Exception ex) => ex switch
    {
        HttpRequestException => true,
        TaskCanceledException => true, // HttpClient timeout
        InvalidDataException => true,  // malformed protobuf / zip
        System.Text.Json.JsonException => true,
        AggregateException agg => agg.InnerExceptions.All(IsUpstreamFailure),
        _ => false
    };
}
