# KrakowOpenData.Client

Typed .NET client for the Kraków Open Data API: public transport, air quality, weather, river levels,
warnings, districts, public amenities, city procedures, NFZ waiting lists and the city's Open Data tables.

```csharp
// ASP.NET Core / Blazor / worker
builder.Services.AddKrakowOpenDataClient("https://your-api-host/");

public class MyService(IEnvironmentClient environment)
{
    public Task<IReadOnlyList<AirQualityDto>> Air() => environment.GetAirQualityAsync();
}

// Console app or script
using var api = KrakowOpenDataClient.Create("https://your-api-host/");
var departures = await api.Mobility.GetDeparturesAsync("T:123");
```

Errors throw `KrakowApiException`; `IsUpstreamUnavailable` is true when a public source is down.
GET requests are retried twice on 502/503/504 by default (`MaxRetries`).
