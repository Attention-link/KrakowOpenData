using System.Threading.RateLimiting;
using KrakowOpenData.Infrastructure.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace KrakowOpenData.Api.Endpoints;

/// <summary>
/// Per-IP limit on everything that writes under <c>/api/safety</c> (reports, "still true" confirmations, planner actions):
/// <see cref="SafetyOptions.WriteRequestsPerMinute"/> requests per minute in a fixed window, then 429 in the same
/// problem style as the per-device report limit. Reads are not limited.
/// </summary>
public static class SafetyRateLimit
{
    public static IServiceCollection AddSafetyRateLimiter(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var limit = http.RequestServices.GetRequiredService<IOptions<SafetyOptions>>().Value.WriteRequestsPerMinute;
                if (limit <= 0 || !IsSafetyWrite(http.Request)) return RateLimitPartition.GetNoLimiter(string.Empty);
                return RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                });
            });
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await Results.Problem(
                    title: "Too many requests",
                    detail: "Too many changes from this address. Please wait a minute and try again.",
                    statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context.HttpContext);
            };
        });

    private static bool IsSafetyWrite(HttpRequest request) =>
        (HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsDelete(request.Method) || HttpMethods.IsPatch(request.Method))
        && request.Path.StartsWithSegments("/api/safety", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The caller's address: Cloudflare's <c>CF-Connecting-IP</c>, else the first <c>X-Forwarded-For</c> entry, else the
    /// socket address. The headers are trusted because the API is published behind Cloudflare; exposed directly, a client
    /// could rotate them to get a fresh window, so the origin should only accept traffic from the proxy.
    /// </summary>
    internal static string ClientIp(HttpContext http)
    {
        var headers = http.Request.Headers;
        var ip = headers["CF-Connecting-IP"].ToString().Trim();
        if (ip.Length == 0) ip = headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
        if (ip.Length == 0) ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return ip.Length > 64 ? ip[..64] : ip;   // a partition key per address; never let a header make it huge
    }
}
