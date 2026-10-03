using KrakowOpenData.Application.Catalog;

namespace KrakowOpenData.Application.Abstractions;

/// <summary>Reads rows of a city Open Data table. Values are kept as published (text).</summary>
public interface IOpenDataTableReader
{
    Task<OpenDataTableContent> ReadAsync(OpenDataTable table, int maxRows, CancellationToken cancellationToken = default);
}

/// <summary>Columns in first-seen order; <see cref="Truncated"/> is true when more rows exist upstream.</summary>
public sealed record OpenDataTableContent(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows,
    bool Truncated,
    string Source);
