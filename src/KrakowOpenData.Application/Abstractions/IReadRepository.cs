using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Application.Abstractions;

/// <summary>
/// Read-only repository over one kind of public-data record. All public data is read-only from
/// our side, so there is no write interface. Implementations live in Infrastructure and may read
/// from live feeds, downloaded files or bundled seed data.
/// </summary>
public interface IReadRepository<T> where T : class, IEntity
{
    Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default);
}
