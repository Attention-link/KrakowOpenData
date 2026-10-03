using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KrakowOpenData.Client.Http;

/// <summary>The only class that talks HTTP. Area clients build URLs; this sends them and reads JSON.</summary>
internal sealed class ApiTransport(HttpClient http) : IApiTransport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<T> GetAsync<T>(string relativeUrl, CancellationToken ct)
    {
        using var response = await http.GetAsync(relativeUrl, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
               ?? throw new KrakowApiException(response.StatusCode, $"Empty response from {relativeUrl}.");
    }

    public async Task<T?> GetOrNullAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(relativeUrl, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    /// <summary>Turns the API's ProblemDetails (title / detail / validation errors) into a readable message.</summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var message = $"{(int)response.StatusCode} {response.ReasonPhrase}";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var parts = new List<string>();
            if (root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t) parts.Add(t);
            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d) parts.Add(d);
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                parts.AddRange(errors.EnumerateObject()
                    .SelectMany(e => e.Value.EnumerateArray().Select(v => $"{e.Name}: {v.GetString()}")));
            }

            if (parts.Count > 0) message = string.Join(" ", parts);
        }
        catch (JsonException)
        {
            // Not ProblemDetails; keep the status line.
        }

        throw new KrakowApiException(response.StatusCode, message);
    }
}
