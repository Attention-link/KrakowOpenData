using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.PublicServices;

namespace KrakowOpenData.Infrastructure.Nfz;

/// <summary>Offline stand-in used in sample mode. Invented providers, clearly labelled.</summary>
public sealed class SampleWaitingListSource : IWaitingListSource
{
    public const string SourceName = "SAMPLE (not real)";

    private static readonly WaitingListEntry[] Entries =
    [
        new("sample-1", "PORADNIA ORTOPEDYCZNA", "PRZYCHODNIA PRZYKŁADOWA A", "Poradnia ortopedyczna", "UL. PRZYKŁADOWA 1", "KRAKÓW", "12 000 00 01",
            new GeoPoint(50.0617, 19.9373), false, 120, 45, null, "2026-08", true, true, false, true, SourceName),
        new("sample-2", "PORADNIA ORTOPEDYCZNA", "SZPITAL PRZYKŁADOWY B", "Poradnia urazowo-ortopedyczna", "UL. PRZYKŁADOWA 2", "KRAKÓW", "12 000 00 02",
            new GeoPoint(50.0717, 20.0378), false, 640, 180, null, "2026-08", true, false, true, true, SourceName),
        new("sample-3", "PORADNIA KARDIOLOGICZNA", "PRZYCHODNIA PRZYKŁADOWA C", "Poradnia kardiologiczna", "UL. PRZYKŁADOWA 3", "KRAKÓW", null,
            null, false, 75, 30, null, "2026-08", null, null, null, null, SourceName)
    ];

    public Task<IReadOnlyList<WaitingListEntry>> SearchAsync(string benefit, bool urgent, CancellationToken cancellationToken = default)
    {
        var needle = TextNormalizer.Normalize(benefit);
        IReadOnlyList<WaitingListEntry> result = Entries
            .Where(e => TextNormalizer.Normalize(e.Benefit).Contains(needle, StringComparison.Ordinal))
            .Select(e => e with { Urgent = urgent })
            .ToList();
        return Task.FromResult(result);
    }
}
