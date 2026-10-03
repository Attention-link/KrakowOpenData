using System.Globalization;

namespace KrakowOpenData.Client.Http;

/// <summary>Builds relative URLs with escaped, culture-invariant query values; empty values are left out.</summary>
internal static class Url
{
    public static string Build(string path, params (string Name, object? Value)[] query)
    {
        var parts = query
            .Where(q => q.Value is not null && !(q.Value is string s && string.IsNullOrWhiteSpace(s)))
            .Select(q => $"{q.Name}={Uri.EscapeDataString(Format(q.Value!))}")
            .ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }

    public static string Segment(string value) => Uri.EscapeDataString(value);

    private static string Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };
}
