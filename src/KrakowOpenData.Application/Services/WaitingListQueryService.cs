using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Mapping;
using KrakowOpenData.Contracts;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for NFZ waiting lists in Kraków (Public services category).</summary>
public sealed class WaitingListQueryService(IWaitingListSource source)
{
    /// <summary>Shortest average wait first; providers without statistics go last.</summary>
    public async Task<IReadOnlyList<WaitingListDto>> SearchAsync(string benefit, bool urgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(benefit)) return [];

        return (await source.SearchAsync(benefit.Trim(), urgent, ct))
            .OrderBy(w => w.AverageWaitDays ?? int.MaxValue)
            .ThenBy(w => w.Provider, StringComparer.CurrentCulture)
            .Select(w => w.ToDto())
            .ToList();
    }
}
