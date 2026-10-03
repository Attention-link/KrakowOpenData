namespace KrakowOpenData.Application.Abstractions;

/// <summary>A reusable filter that repositories apply to their records.</summary>
public interface ISpecification<in T>
{
    bool IsSatisfiedBy(T candidate);
}

/// <summary>Specification built from a predicate, with simple composition.</summary>
public sealed class Specification<T>(Func<T, bool> predicate) : ISpecification<T>
{
    public static Specification<T> All { get; } = new(_ => true);

    public bool IsSatisfiedBy(T candidate) => predicate(candidate);

    public Specification<T> And(ISpecification<T> other) =>
        new(x => IsSatisfiedBy(x) && other.IsSatisfiedBy(x));

    public Specification<T> Or(ISpecification<T> other) =>
        new(x => IsSatisfiedBy(x) || other.IsSatisfiedBy(x));

    public Specification<T> Not() => new(x => !IsSatisfiedBy(x));
}
