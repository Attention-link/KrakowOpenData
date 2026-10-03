namespace KrakowOpenData.Infrastructure.Options;

/// <summary>Configuration section "Safety:Routing": the street-routing service used for walking routes.</summary>
public sealed class RoutingOptions
{
    public const string SectionName = "Safety:Routing";

    /// <summary>
    /// An OSRM-compatible service with a foot profile. The default is the public OpenStreetMap Germany foot router, which is
    /// fine for a prototype (fair use, no guarantee). Use your own OSRM or Valhalla instance in production.
    /// </summary>
    public string BaseUrl { get; set; } = "https://routing.openstreetmap.de/routed-foot/";
}
