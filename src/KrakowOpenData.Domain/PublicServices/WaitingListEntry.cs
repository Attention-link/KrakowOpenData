using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.PublicServices;

/// <summary>
/// One provider's waiting list for an NFZ-funded service (e.g. an orthopaedic clinic), from the
/// NFZ "terminy leczenia" API. Wait times are the provider's own statistics, in days.
/// </summary>
public sealed record WaitingListEntry(
    string Id,
    string Benefit,
    string Provider,
    string? Place,
    string? Address,
    string? Locality,
    string? Phone,
    GeoPoint? Location,
    bool Urgent,
    int? PeopleWaiting,
    int? AverageWaitDays,
    string? FirstAvailableDate,
    string? StatisticsUpdated,
    bool? Toilet,
    bool? Ramp,
    bool? CarPark,
    bool? Elevator,
    string Source) : IEntity
{
    /// <summary>Ramp or elevator reported: a rough "step-free access" flag.</summary>
    public bool? StepFree => Ramp is null && Elevator is null ? null : Ramp == true || Elevator == true;
}
