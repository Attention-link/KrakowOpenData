using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.UrbanSpace;

/// <summary>One of Kraków's 18 administrative districts (dzielnice). Id is the Roman numeral.</summary>
public sealed record District(
    string Id,
    int Number,
    string Name,
    int? RegisteredPopulation,
    string? PopulationNote,
    string Source) : IEntity;
