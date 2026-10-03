using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.PublicServices;

/// <summary>An official procedure ("karta usługi") published on the city's BIP.</summary>
public sealed record CityServiceCard(
    string Id,
    string Title,
    string Topic,
    string Summary,
    IReadOnlyList<string> Steps,
    string? Office,
    string? Address,
    string? OpeningHours,
    string? Phone,
    string? Fee,
    string SourceUrl,
    IReadOnlyList<string> Keywords) : IEntity
{
    /// <summary>
    /// Relevance of the card for a free-text query: title hits weigh most, then keywords,
    /// topic and summary. 0 means no match. Diacritics and case are ignored.
    /// </summary>
    public int RelevanceFor(string? query)
    {
        var words = TextNormalizer.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;

        var title = TextNormalizer.Normalize(Title);
        var topic = TextNormalizer.Normalize(Topic);
        var summary = TextNormalizer.Normalize(Summary);
        var keywords = Keywords.Select(TextNormalizer.Normalize).ToArray();
        var id = TextNormalizer.Normalize(Id);

        var score = 0;
        foreach (var word in words)
        {
            if (id == word) score += 10;
            if (title.Contains(word, StringComparison.Ordinal)) score += 5;
            if (keywords.Any(k => k.Contains(word, StringComparison.Ordinal))) score += 3;
            if (topic.Contains(word, StringComparison.Ordinal)) score += 2;
            if (summary.Contains(word, StringComparison.Ordinal)) score += 1;
        }

        return score;
    }
}
