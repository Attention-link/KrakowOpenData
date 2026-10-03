namespace KrakowOpenData.Domain.Mobility;

public enum TransportMode
{
    Tram,
    Metro,
    Rail,
    Bus,
    Other
}

public static class TransportModeExtensions
{
    /// <summary>Maps a GTFS route_type (basic or extended) to a mode.</summary>
    public static TransportMode FromGtfsRouteType(int routeType) => routeType switch
    {
        0 or (>= 900 and < 1000) => TransportMode.Tram,
        1 or (>= 400 and < 500) => TransportMode.Metro,
        2 or (>= 100 and < 200) => TransportMode.Rail,
        3 or (>= 700 and < 800) => TransportMode.Bus,
        _ => TransportMode.Other
    };
}
