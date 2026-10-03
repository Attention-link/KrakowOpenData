using KrakowOpenData.Domain.PublicServices;

namespace KrakowOpenData.Application.Abstractions;

/// <summary>NFZ waiting lists. Needs a search term, as the upstream API does not list everything at once.</summary>
public interface IWaitingListSource
{
    /// <param name="benefit">Part of the benefit name, e.g. "ortoped" or "PORADNIA KARDIOLOGICZNA".</param>
    /// <param name="urgent">True for urgent cases (NFZ case 2), false for stable cases (case 1).</param>
    Task<IReadOnlyList<WaitingListEntry>> SearchAsync(string benefit, bool urgent, CancellationToken cancellationToken = default);
}
