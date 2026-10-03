namespace KrakowOpenData.Domain.Common;

/// <summary>Every record exposed through a repository has a stable string identifier.</summary>
public interface IEntity
{
    string Id { get; }
}
