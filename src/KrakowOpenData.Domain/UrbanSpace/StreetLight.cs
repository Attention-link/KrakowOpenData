using KrakowOpenData.Domain.Common;

namespace KrakowOpenData.Domain.UrbanSpace;

/// <summary>
/// A street lamp mapped in OpenStreetMap (<c>highway=street_lamp</c>). Only the position is always known;
/// type, mount, height and operator are filled in for a minority of lamps.
/// </summary>
public sealed record StreetLight(
    string Id,
    GeoPoint Location,
    StreetLightTechnology Technology,
    string? Mount,
    int? LightCount,
    double? HeightMeters,
    string? Operator,
    string? Reference,
    string Source) : IEntity
{
    /// <summary>Maps OSM <c>lamp_type</c> / <c>light:method</c> values to a technology.</summary>
    public static StreetLightTechnology TechnologyFrom(string? lampType, string? lightMethod)
    {
        foreach (var value in new[] { lightMethod, lampType })
        {
            var v = value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(v)) continue;
            if (v.Contains("led")) return StreetLightTechnology.Led;
            if (v.Contains("sodium")) return StreetLightTechnology.Sodium;
            if (v.Contains("mercury")) return StreetLightTechnology.Mercury;
            if (v.Contains("metal") || v.Contains("halide")) return StreetLightTechnology.MetalHalide;
            if (v is "electric" or "street_lamp" or "yes") continue; // says nothing about the technology
            return StreetLightTechnology.Other;
        }

        return StreetLightTechnology.Unknown;
    }
}

public enum StreetLightTechnology
{
    Unknown,
    Led,
    Sodium,
    Mercury,
    MetalHalide,
    Other
}
