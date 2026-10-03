using System.Globalization;
using System.Text.Json;

namespace KrakowOpenData.Infrastructure.OpenDataPortal;

/// <summary>One page of a city Open Data table plus the cursor for the next page (null on the last page).</summary>
public sealed record OpenDataPage(IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows, string? NextAfter);

/// <summary>
/// Parses api.um.krakow.pl responses: <c>{"value":[{...}], "nextLink":"https://internal-host/...?$after=..."}</c>.
/// The nextLink points at an internal host, so only its <c>$after</c> cursor is reused against the public URL.
/// Numbers come back as floats (2023.0); whole numbers are shown without the ".0".
/// </summary>
public static class OpenDataPortalParser
{
    public static OpenDataPage ParsePage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        var items = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var v) ? v : root;
        if (items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var row = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject()) row[property.Name] = Text(property.Value);
                rows.Add(row);
            }
        }

        string? next = null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("nextLink", out var link) && link.ValueKind == JsonValueKind.String)
        {
            next = AfterCursor(link.GetString());
        }

        return new OpenDataPage(rows, next);
    }

    /// <summary>Extracts the raw (still URL-encoded) <c>$after</c> value from a nextLink.</summary>
    public static string? AfterCursor(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink)) return null;
        var q = nextLink.IndexOf('?');
        if (q < 0) return null;

        foreach (var part in nextLink[(q + 1)..].Split('&'))
        {
            foreach (var prefix in new[] { "$after=", "%24after=" })
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var value = part[prefix.Length..];
                    return value.Length == 0 ? null : value;
                }
            }
        }

        return null;
    }

    /// <summary>Columns in first-seen order across all rows.</summary>
    public static IReadOnlyList<string> Columns(IEnumerable<IReadOnlyDictionary<string, string?>> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key)) columns.Add(key);
            }
        }

        return columns;
    }

    internal static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => Number(value),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText()
    };

    private static string Number(JsonElement value)
    {
        if (value.TryGetInt64(out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
        var d = value.GetDouble();
        if (Math.Abs(d) < 1e15 && d == Math.Floor(d)) return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
