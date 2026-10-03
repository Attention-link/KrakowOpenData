# Kraków Open Data

One API, a .NET client package and a web UI over Kraków's live public data, grouped by topic: mobility, environment,
climate & crisis, urban space, public services and society. Built as a hackathon starting point: pick an idea,
keep what you need, delete the rest.

## What it does

- **API** (`KrakowOpenData.Api`) – REST endpoints per category, documented with OpenAPI (Swagger UI at http://localhost:5080/swagger).
- **Client** (`KrakowOpenData.Client` + `KrakowOpenData.Contracts`) – a typed .NET client, packaged for NuGet, so other solutions can use the data.
- **Web** (`KrakowOpenData.Web`) – Blazor pages for each dataset, built on the client like any other consumer. Every page is in
  **Polish, English and Ukrainian** (switcher in the sidebar). City data itself (stop names, alert text, table columns) is shown as published.

## Data sources

Everything is live, public data that needs no API key. The API fetches it from the source and caches it.

| Category | Data | Source | Refreshed |
| --- | --- | --- | --- |
| Mobility | Stops, lines, timetables | ZTP Kraków GTFS | 6 h |
| Mobility | Vehicle positions, trip updates, disruptions | ZTP Kraków GTFS-Realtime | 20 s |
| Mobility | P+R car parks (capacity, EV spaces, hours) | OpenStreetMap (Overpass API) | daily |
| Environment | Weather | IMGW-PIB | 10 min |
| Environment | Air quality (PM2.5, PM10, NO₂, Polish index) | GIOŚ | 20 min |
| Climate & crisis | River levels with warning/alarm thresholds | IMGW-PIB | 10 min |
| Climate & crisis | Meteorological and hydrological warnings | IMGW-PIB | 10 min |
| Urban space | Districts with population | City of Kraków Open Data API | 6 h |
| Urban space | AEDs, drinking water, toilets, EV chargers, bike parking | OpenStreetMap (Overpass API) | daily |
| Urban space | Street lights (~27,000: position; LED/sodium, mount, height where mapped) | OpenStreetMap (Overpass API) | daily |
| Public services | City service cards (every BIP procedure) | City of Kraków Open Data API | 6 h |
| Public services | NFZ treatment waiting lists | NFZ API | 1 h per search |
| Society, services, environment | 45 city tables: residents, jobs, tourism, culture, sport events, schools, nurseries, health, parks, vehicles, lost property | City of Kraków Open Data API | 6 h |

If a source is down, its endpoints answer **503** and the rest keep working; cached data is served while a refresh fails.

The OpenStreetMap datasets (street lights, amenities, P+R) are large, so the API downloads them **in the background**
when it starts and once a day, trying mirror servers if the main one is busy. The last download is saved to
`%LOCALAPPDATA%\KrakowOpenData\cache` (change with `KrakowData:CacheDirectory`), so after a restart they are available
immediately. On the very first start they take a minute or two; until then their endpoints answer 503 "Data is still
loading" with a `Retry-After` header.

## Structure

```
src/
  KrakowOpenData.Domain          entities and business rules
  KrakowOpenData.Application     use cases, repository interfaces, entity → DTO mapping, data catalog
  KrakowOpenData.Infrastructure  one client per public source (GTFS, IMGW, GIOŚ, NFZ, OSM, city API), caching, DI
  KrakowOpenData.Api             minimal API + OpenAPI
  KrakowOpenData.Contracts       DTOs returned by the API (no dependencies; NuGet package)
  KrakowOpenData.Client          typed .NET client (NuGet package)
  KrakowOpenData.Web             Blazor UI (uses the client)
tests/                           xUnit tests per layer; API and client tests run against in-memory fakes
.github/workflows/               packs and publishes the client packages
```

Dependencies point inwards: Web → Client → Contracts; Api → Infrastructure → Application → Domain + Contracts.

## Dependencies

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer
- Visual Studio 2022+ (ASP.NET workload) or any editor with the .NET CLI
- Internet access (all data is live)
- NuGet packages (restored automatically): Swashbuckle.AspNetCore (API docs), Microsoft.Extensions.Http (client);
  tests: xUnit, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing
- Optional: Docker

## How to run

The solution has **two apps that must both be running**: the API (port 5080) and the web UI (port 5090).
The UI only shows data while the API is up. The first request takes up to a minute while the timetables download.

There are **three ways** to start them: **Visual Studio**, the **command line** or **Docker**.
Pick **one**; you don't need to do all three.

### Option A: Visual Studio

1. Open `KrakowOpenData.sln`. NuGet packages restore automatically.
2. In Solution Explorer, right-click the solution → **Configure Startup Projects…**
3. Choose **Multiple startup projects**, set `KrakowOpenData.Api` and `KrakowOpenData.Web` to **Start**, then click **OK**.
4. Press **F5**. Two browser tabs open: the API docs (Swagger) and the UI at http://localhost:5090.

You only do steps 2–3 once; after that, F5 starts both.

### Option B: Command line

Needs two terminals, both opened in the solution folder.

1. **Terminal 1** – start the API and leave it running:
   ```bash
   dotnet run --project src/KrakowOpenData.Api
   ```
2. **Terminal 2** – start the UI and leave it running:
   ```bash
   dotnet run --project src/KrakowOpenData.Web
   ```
3. Open http://localhost:5090.

`dotnet run` builds automatically, so a separate `dotnet build` isn't needed.

### Option C: Docker

1. From the solution folder run:
   ```bash
   docker compose up --build
   ```
2. Open http://localhost:5090 (UI) or http://localhost:5080/swagger (API docs).

This one command starts both apps.

### Running the tests (optional)

Not needed to run the apps. From the solution folder: `dotnet test`, or **Test → Run All Tests** in
Visual Studio. The tests replace every public source with in-memory fakes, so they need no internet.

## Consuming the data

The API is the single entry point. Pick whichever fits your app:

| You are building… | Use |
| --- | --- |
| A .NET app (ASP.NET Core, Blazor, MAUI, worker, console) | The `KrakowOpenData.Client` NuGet package |
| Anything else (JavaScript/TypeScript, Python, Power BI, …) | The REST API, described by the OpenAPI spec |

### Any language: REST + OpenAPI

- Interactive docs: http://localhost:5080/swagger (try every endpoint in the browser).
- Machine-readable spec: http://localhost:5080/swagger/v1/swagger.json.
- Generate a typed client from the spec, for example:
  ```bash
  npx @openapitools/openapi-generator-cli generate -i http://localhost:5080/swagger/v1/swagger.json -g typescript-fetch -o ./krakow-client
  ```
- Errors are [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) JSON: 400 invalid input, 404 not found,
  503 a public source is unavailable (retry later).
- `src/KrakowOpenData.Api/KrakowOpenData.Api.http` has a ready request for every endpoint (Visual Studio: **Send request**).

### .NET: the client package

**1. Add the package.** Pick the option that matches how it's distributed (see [Publishing the client](#publishing-the-client)):

```bash
# From GitHub Packages (after adding the feed once, see below)
dotnet add package KrakowOpenData.Client

# Or, inside this repo / a local build
dotnet add reference path/to/src/KrakowOpenData.Client/KrakowOpenData.Client.csproj
```

**2a. With dependency injection** (ASP.NET Core, Blazor, workers) – one line in `Program.cs`:

```csharp
using KrakowOpenData.Client;

builder.Services.AddKrakowOpenDataClient("http://localhost:5080/");
// or: builder.Services.AddKrakowOpenDataClient(o => { o.BaseAddress = new Uri(url); o.MaxRetries = 3; });
```

Then inject the whole client, or only the area you need:

```csharp
public class AirQualityAlert(IEnvironmentClient environment)          // just one area
{
    public async Task<bool> IsBadAsync() =>
        (await environment.GetAirQualityAsync()).Any(a => a.Band is "Poor" or "VeryPoor");
}

public class Dashboard(IKrakowOpenDataClient api)                     // everything
{
    public Task<IReadOnlyList<DepartureDto>> NextTrams(string stopId) => api.Mobility.GetDeparturesAsync(stopId, limit: 5);
}
```

**2b. Without dependency injection** (console apps, scripts, notebooks):

```csharp
using var api = KrakowOpenDataClient.Create("http://localhost:5080/");

var aeds = await api.Urban.GetAmenitiesAsync("Defibrillator", lat: 50.0617, lon: 19.9373, radiusMeters: 500);
var lamps = await api.Urban.GetStreetLightSummaryAsync(lat: 50.0617, lon: 19.9373, radiusMeters: 300); // count, per km², LED share
var nurseries = await api.OpenData.GetRowsAsync("nurseries-public");
```

**Areas:** `Catalog`, `Mobility`, `Environment`, `Crisis`, `Urban`, `Services`, `OpenData` – each is also an interface
(`ICatalogClient`, `IMobilityClient`, …) you can inject on its own and fake in your tests.

**Errors and retries:** non-success responses throw `KrakowApiException` with the API's message;
`IsUpstreamUnavailable` is true for 503. GET requests are retried twice (1 s, 2 s) on 502/503/504 and
network errors. Single-item methods (`GetStopAsync`, `GetDistrictAsync`, …) return `null` for 404.

**Types:** all DTOs (`StopDto`, `AirQualityDto`, …) live in the `KrakowOpenData.Contracts` namespace and package,
which the client package brings in automatically.

### Publishing the client

The packages are `KrakowOpenData.Contracts` and `KrakowOpenData.Client` (the client depends on the contracts).

- **Local feed** (quickest, for a team on one network share or one machine):
  ```bash
  dotnet pack src/KrakowOpenData.Contracts -c Release -o ./artifacts
  dotnet pack src/KrakowOpenData.Client -c Release -o ./artifacts
  # On the consuming machine, once (use the full path to the folder):
  dotnet nuget add source C:/path/to/artifacts --name krakow-local
  ```
- **GitHub Packages** (recommended once the repo is on GitHub): push a version tag and the
  `publish-client` workflow (`.github/workflows/publish-client.yml`) tests, packs and publishes both packages:
  ```bash
  git tag v0.1.0 && git push origin v0.1.0
  ```
  Consumers add the feed once (a GitHub token with `read:packages`):
  ```bash
  dotnet nuget add source "https://nuget.pkg.github.com/OWNER/index.json" --name github --username YOUR_GITHUB_USER --password YOUR_TOKEN
  ```
- **nuget.org**: same packages; push with `dotnet nuget push artifacts/*.nupkg --source https://api.nuget.org/v3/index.json --api-key KEY`.

The version comes from `Directory.Build.props` (0.1.0) or the tag (`-p:Version=…`).

## Data not available

Public data we looked for but could not get from any open source.

| Data | Why it's not available |
| --- | --- |
| District boundaries, addresses, streets, zoning plans (MPZP), flood hazard zones | MSIP Kraków's map services (WFS/REST) returned 404 when checked. |
| City bike availability | No public GBFS or other feed from the operators. |
| P+R occupancy (free spaces) | No public live feed. |
| Official street-light register, lamp status (on/off, faults), energy use | ZDMK publishes no open dataset; street lights above come from OpenStreetMap and may be incomplete. |
| Live transit delays | ZTP's TripUpdates feed mostly leaves the delay field empty. |
| NFZ first available appointment date | The NFZ API returns this field empty. |
| Public transport ridership, cycling counters, city information system (SIM) | Published only as downloadable files on the Open Data portal, with no API. |
| New-flat prices | Only as hundreds of separate developer files on dane.gov.pl, with no Kraków summary. |
