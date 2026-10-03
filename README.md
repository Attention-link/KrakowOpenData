# Kraków Open Data

One API and a web UI over Kraków's public data, grouped by topic: mobility, environment,
climate & crisis, urban space and public services. Built as a hackathon starting point: pick an idea,
keep what you need, delete the rest.

## What it does

- **API** (`KrakowOpenData.Api`) – REST endpoints per category, plus `GET /api/catalog` listing every dataset and its status.
- **Web** (`KrakowOpenData.Web`) – Blazor pages for each dataset: stops and live departures, a vehicle map, disruptions, weather, air quality, river levels, warnings, districts, city service cards, NFZ waiting lists, and a viewer for 44 city Open Data tables.
  Every page is available in **Polish, English and Ukrainian** (switcher at the top of the sidebar; the choice is remembered per browser, and first-time visitors get their browser's language).
  City data itself (stop names, alert and service-card text) is shown as published.
- **Data sources**
  - Live (no API keys needed):
    - ZTP Kraków GTFS + GTFS-Realtime (buses, trams, agglomeration)
    - IMGW weather, river gauges around Kraków, meteorological and hydrological warnings
    - GIOŚ air quality (PM2.5, PM10, NO₂ and the official index for every Kraków station)
    - NFZ treatment waiting lists for Kraków providers
    - City of Kraków Open Data API: 44 tables (residents, jobs, tourism, culture, sport events, schools, nurseries, health, parks, vehicles, lost property)
  - Seed (copied from official pages): P+R car parks, districts, service cards, river readings from the 15 Sep 2024 storm.
  - Sample mode only (invented, labelled `SAMPLE`): an offline version of every source for demos and tests.

## Structure

```
src/
  KrakowOpenData.Domain          entities and business rules
  KrakowOpenData.Application     repository interfaces, services, DTOs, data catalog
  KrakowOpenData.Infrastructure  data sources (GTFS, GTFS-RT, IMGW, seed JSON), caching, DI
  KrakowOpenData.Api             minimal API
  KrakowOpenData.Web             Blazor UI (calls the API over HTTP)
tests/                           xUnit tests for each layer, API integration tests, translation checks
```

## Dependencies

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer
- Visual Studio 2022+ (ASP.NET workload) or any editor with the .NET CLI
- Internet access for live data (not needed in sample mode)
- Test projects only: xUnit, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing (restored from NuGet)
- Optional: Docker

The app projects use no NuGet packages.

## How to run

The solution has **two apps that must both be running**: the API (port 5080) and the web UI (port 5090).
The UI only shows data while the API is up.

There are **three ways** to start them: **Visual Studio**, the **command line** or **Docker**.
Pick **one**; you don't need to do all three.

### Choose a data mode

| Mode | What you get | Needs internet |
| --- | --- | --- |
| `live` (default) | Real data from ZTP, IMGW, GIOŚ, NFZ and the city's Open Data API | Yes |
| `sample-data` | Built-in sample network and data, no downloads | No |

In live mode the first request takes up to a minute while the timetables download. Use sample mode
for offline demos or if live data fails.

### Option A: Visual Studio

1. Open `KrakowOpenData.sln`. NuGet packages restore automatically.
2. In Solution Explorer, right-click the solution → **Configure Startup Projects…**
3. Choose **Multiple startup projects**, set `KrakowOpenData.Api` and `KrakowOpenData.Web` to **Start**, then click **OK**.
   - To use sample data, set the API's **Launch Profile** column to `sample-data` in the same dialog.
4. Press **F5**. Two browser tabs open: the API catalog and the UI at http://localhost:5090.

You only do steps 2–3 once; after that, F5 starts both.

### Option B: Command line

Needs two terminals, both opened in the solution folder.

1. **Terminal 1** – start the API and leave it running:
   ```bash
   dotnet run --project src/KrakowOpenData.Api --launch-profile live
   ```
   For sample data, use `--launch-profile sample-data` instead.
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
   For sample data: `KRAKOW_SAMPLE=true docker compose up --build`
   (PowerShell: `$env:KRAKOW_SAMPLE="true"; docker compose up --build`).
2. Open http://localhost:5090.

This one command starts both apps.

### Running the tests (optional)

Not needed to run the apps. From the solution folder: `dotnet test`, or **Test → Run All Tests** in
Visual Studio. The tests use sample data and need no internet.

### Trying the API directly (optional)

`src/KrakowOpenData.Api/KrakowOpenData.Api.http` has a request for every endpoint (open it in Visual
Studio and click **Send request**), or browse to http://localhost:5080/api/catalog.

## Data not yet included

Known public sources that are not wired up, and why. The catalog page lists the planned ones too.

| Data | Source | Why it's missing |
| --- | --- | --- |
| AEDs, drinking water, public toilets, EV chargers, bike parking | OpenStreetMap (Overpass API) | Overpass rate-limited our queries (HTTP 429). Works with smaller queries or a downloaded extract. |
| District boundaries, addresses, streets, zoning plans (MPZP), flood zones | MSIP Kraków (WFS/REST) | The service endpoints returned 404 when checked. Download layers by hand and add as GeoJSON. |
| City bikes (availability) | City bike operators | No public GBFS feed found. |
| P+R occupancy | ZTP Kraków | No public live feed; only static facts are seeded. |
| Public transport statistics, cycling counters, city information system | Otwarte Dane Kraków (KMK, rowery, SIM datasets) | Published as files only, no API. Download and add as seed data. |
| Older years of yearly tables (schools, theatres, museums, tourism, residents) | City Open Data API | Only the latest year is listed. Add more rows to `OpenDataTables.cs`. |
| Museums/galleries, cultural centres and sport facility tables | City Open Data API | Exact table names not confirmed. |
| Foreign residents (cudzoziemcy) | City Open Data API | Exact table names not confirmed. |
| Flat prices from developers | dane.gov.pl | Hundreds of separate per-developer files; no Kraków aggregate. |
| First available appointment date | NFZ API | The field is empty in API responses; only wait statistics are shown. |
| Live delay values | ZTP GTFS-Realtime TripUpdates | Mostly empty in the feed; departures fall back to the timetable. |

