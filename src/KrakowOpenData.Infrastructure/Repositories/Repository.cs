using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Infrastructure.DataSources;

namespace KrakowOpenData.Infrastructure.Repositories;

/// <summary>
/// Generic read repository over any <see cref="IDataSource{T}"/>. Public datasets are small enough
/// (tens of thousands of records at most) to filter in memory; swap in a database-backed
/// implementation of <see cref="IReadRepository{T}"/> later without touching the Application layer.
/// </summary>
public sealed class Repository<T>(IDataSource<T> source) : IReadRepository<T> where T : class, IEntity
{
    public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var all = await source.LoadAsync(cancellationToken);
        return all.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        var all = await source.LoadAsync(cancellationToken);
        return specification is null ? all : all.Where(specification.IsSatisfiedBy).ToList();
    }

    public async Task<int> CountAsync(
        ISpecification<T>? specification = null,
        CancellationToken cancellationToken = default)
    {
        var all = await source.LoadAsync(cancellationToken);
        return specification is null ? all.Count : all.Count(specification.IsSatisfiedBy);
    }
}
