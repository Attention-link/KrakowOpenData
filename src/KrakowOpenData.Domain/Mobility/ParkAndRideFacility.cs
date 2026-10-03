using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.Mobility;

/// <summary>A P+R car park run by the city (ZTP Kraków). Unknown values stay null.</summary>
public sealed record ParkAndRideFacility(
    string Id,
    string Name,
    string? Address,
    GeoPoint? Location,
    int? Capacity,
    int? EvChargers,
    int? BikeSpaces,
    string? OpeningHours,
    string? Notes,
    string Source) : IEntity;
