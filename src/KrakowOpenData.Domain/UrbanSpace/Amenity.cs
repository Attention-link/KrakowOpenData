using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.UrbanSpace;

/// <summary>A public amenity mapped in OpenStreetMap (an AED, a water tap, a toilet, …).</summary>
public sealed record Amenity(
    string Id,
    AmenityKind Kind,
    string? Name,
    GeoPoint Location,
    string? Details,
    string Source) : IEntity;

public enum AmenityKind
{
    Defibrillator,
    DrinkingWater,
    Toilets,
    EvCharger,
    BikeParking
}
