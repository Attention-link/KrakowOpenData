using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Catalog;

namespace KrakowOpenData.Api.Tests.Fakes;

/// <summary>Offline stand-in for API tests: three invented rows per table.</summary>
public sealed class FakeOpenDataTableReader : IOpenDataTableReader
{
    public const string SourceName = "TEST";

    public Task<OpenDataTableContent> ReadAsync(OpenDataTable table, int maxRows, CancellationToken cancellationToken = default)
    {
        string[] columns = ["Lp.", "Nazwa", "Dzielnica", "Wartość"];
        var rows = new[]
            {
                ("1", "Przykład A", "I Stare Miasto", "120"),
                ("2", "Przykład B", "XIV Czyżyny", "85"),
                ("3", "Przykład C", "XVIII Nowa Huta", "42")
            }
            .Take(Math.Max(0, maxRows))
            .Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                [columns[0]] = r.Item1,
                [columns[1]] = $"{r.Item2} ({table.Title})",
                [columns[2]] = r.Item3,
                [columns[3]] = r.Item4
            })
            .ToList();

        return Task.FromResult(new OpenDataTableContent(columns, rows, false, SourceName));
    }
}
