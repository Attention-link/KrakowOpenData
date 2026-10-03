using KrakowOpenData.Application.Abstractions;
using KrakowOpenData.Domain.Common;
using KrakowOpenData.Domain.Mobility;

namespace KrakowOpenData.Application.Tests;

/// <summary>Hand-written test doubles; no mocking library needed.</summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

public sealed class InMemoryRepository<T>(params T[] items) : IReadRepository<T> where T : class, IEntity
{
    public List<T> Items { get; } = items.ToList();

    public Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<T>> ListAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<T>>(specification is null ? Items.ToList() : Items.Where(specification.IsSatisfiedBy).ToList());

    public Task<int> CountAsync(ISpecification<T>? specification = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(specification is null ? Items.Count : Items.Count(specification.IsSatisfiedBy));
}

public sealed class FakeScheduleRepository : ITransitScheduleRepository
{
    public List<StopTime> StopTimes { get; } = [];
    public Dictionary<string, TransitTrip> Trips { get; } = new();
    public HashSet<(string ServiceId, DateOnly Date)> ActiveDays { get; } = [];

    public Task<IReadOnlyList<StopTime>> GetStopTimesAtStopAsync(string stopId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StopTime>>(StopTimes.Where(s => s.StopId == stopId).OrderBy(s => s.Departure).ToList());

    public Task<IReadOnlyList<StopTime>> GetStopTimesForTripAsync(string tripId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StopTime>>(StopTimes.Where(s => s.TripId == tripId).OrderBy(s => s.StopSequence).ToList());

    public Task<TransitTrip?> GetTripAsync(string tripId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Trips.GetValueOrDefault(tripId));

    public Task<bool> IsServiceActiveAsync(string serviceId, DateOnly date, CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveDays.Contains((serviceId, date)));

    public HashSet<string> NightStops { get; } = [];

    public Task<IReadOnlySet<string>> GetNightServiceStopIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<string>>(NightStops);
}
