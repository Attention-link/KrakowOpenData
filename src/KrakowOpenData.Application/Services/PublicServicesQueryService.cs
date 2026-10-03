using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Application.Mapping;
using KrakowOpenData.Contracts;
using KrakowOpenData.Domain.PublicServices;

namespace KrakowOpenData.Application.Services;

/// <summary>Use cases for city procedures (Public services category).</summary>
public sealed class PublicServicesQueryService(IReadRepository<CityServiceCard> cards)
{
    /// <summary>Ranked search; an empty query returns every card alphabetically.</summary>
    public async Task<IReadOnlyList<ServiceCardDto>> SearchAsync(string? query, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var all = await cards.ListAsync(cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(query))
        {
            return all.OrderBy(c => c.Title, StringComparer.CurrentCulture)
                .Take(limit)
                .Select(c => c.ToDto())
                .ToList();
        }

        return all
            .Select(c => (Card: c, Score: c.RelevanceFor(query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Card.Title, StringComparer.CurrentCulture)
            .Take(limit)
            .Select(x => x.Card.ToDto(x.Score))
            .ToList();
    }

    public async Task<ServiceCardDto?> GetAsync(string id, CancellationToken ct = default) =>
        (await cards.GetByIdAsync(id, ct))?.ToDto();
}
