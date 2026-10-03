using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Catalog;
using KrakowOpenData.Application.Contracts;
using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for the generic tables of the City of Kraków Open Data API.</summary>
public sealed class OpenDataPortalService(IOpenDataTableReader reader)
{
    public const int DefaultRows = 500;
    public const int MaxRows = 5000;

    public IReadOnlyList<OpenDataTableDto> ListTables(DataCategory? category = null) =>
        OpenDataTables.All
            .Where(t => category is null || t.Category == category)
            .Select(t => t.ToDto())
            .ToList();

    /// <summary>Returns null when the key is unknown.</summary>
    public async Task<OpenDataRowsDto?> GetRowsAsync(string key, int? limit, string? query, CancellationToken ct = default)
    {
        var table = OpenDataTables.Find(key);
        if (table is null) return null;

        var max = Math.Clamp(limit ?? DefaultRows, 1, MaxRows);
        var content = await reader.ReadAsync(table, max, ct);

        IEnumerable<IReadOnlyDictionary<string, string?>> rows = content.Rows;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var needle = TextNormalizer.Normalize(query);
            rows = rows.Where(r => r.Values.Any(v => v is not null && TextNormalizer.Normalize(v).Contains(needle, StringComparison.Ordinal)));
        }

        return new OpenDataRowsDto(table.Key, table.Title, table.Category.ToString(), table.ApiUrl,
            content.Columns, rows.Take(max).ToList(), content.Truncated, content.Source);
    }
}
