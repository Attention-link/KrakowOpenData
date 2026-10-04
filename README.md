# Kompas Krakowa (Kraków Compass)

**One-stop open data library for Kraków.** Live public data (transport, environment, crisis warnings, urban space, public
services) behind one open API, with a first app on top that answers: *where in Kraków is it too hot, too dark or too poorly
served, and what should the city do about it?*

Built for HackYeah 2026. The repository started as "Kraków Open Data" (the API, client and data pages below) and the app on
top of it is called Kompas Krakowa.

## Goal

Become Kraków's shared, open data layer for everyday conditions and for crises:

- **One place for the data.** Every dataset reachable through one documented open API and a typed .NET client, so other apps, researchers and the city can reuse it.
- **Residents and agencies on one map.** Resident reports now; later accident and crime reports from residents and from the agencies that hold the official records (police, roads authority, emergency services), each labelled by source and shown only where agencies make data available.
- **Crisis use.** Warnings, alerts, reports and planner tools that work in a heatwave today and can serve floods, air pollution or outages.
- **Honest about gaps.** Scores show how many features are mapped and warn where data is thin; city planners are meant to complete the missing data in a pilot.

## MVP (what works now)

The MVP proves the model on **night safety, heat, flood and air**:

- **Open API** with Swagger docs: data per topic, plus scores for any point, the whole city grid, nearby places, street routes, how every score is built, reports, alerts, planner summary and address search.
- **Resident app** (installable, offline, English/Polish/Ukrainian): map with streets and places, a heat-relief score (0 very hot – 100 plenty of relief) and a night-safety score (0 unsafe – 100 safe), plus flood and clean-air scores with explanations of weights and sources, fastest versus safer/cooler street routes, address search, reporting with neighbour confirmation, alerts for "my area".
- **Planner dashboard**: ranked areas to act on, gap charts, clickable figures with their data sources, reports to verify or resolve, alerts to a map area, simulated agency contact.
- Scores are **environmental (mapped infrastructure plus resident reports), not crime statistics.**

**Remaining for the MVP:** a "possibly missing data" flag on the dashboard, a simple "add a missing place" form for planners, and a final pass on Polish texts and demo data. Everything under "Goal" beyond heat and night safety is future work.

## Quick start

Needs the .NET 8 SDK and an internet connection. Two terminals in the repository folder:

```bash
dotnet run --project src/KrakowOpenData.Api
dotnet run --project src/KrakowOpenData.Web
```

The first start takes 1–2 minutes while the timetable and OpenStreetMap data download.

| Who | Address |
| --- | --- |
| Residents | http://localhost:5090/safety/ |
| City planners | http://localhost:5090/safety/#/planner (also the "Planner dashboard" menu link; opens directly in demo mode) |
| API documentation | http://localhost:5080/swagger |

For Visual Studio, Docker and tests see [How to run](#how-to-run). Demo planner data: **Reports → Add demo reports**.

## What it does

- **API** (`KrakowOpenData.Api`) – REST endpoints per category, documented with OpenAPI (Swagger UI at http://localhost:5080/swagger).
- **Client** (`KrakowOpenData.Client` + `KrakowOpenData.Contracts`) – a typed .NET client, packaged for NuGet, so other solutions can use the data.
- **Web** (`KrakowOpenData.Web`) – Blazor pages for each dataset, built on the client like any other consumer. Every page is in
  **Polish, English and Ukrainian** (switcher in the sidebar). City data itself (stop names, alert text, table columns) is shown as published.
- **Kompas Krakowa app** (API under `/api/safety`, app in `KrakowOpenData.Web/wwwroot/safety`) – the installable, offline-capable web app
  described above. See [Kompas Krakowa app](#kompas-krakowa-app-heat-and-night-safety).

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
| Urban space | Parks (with extent), libraries, pharmacies (with opening hours), hospitals, police stations (used by the safety scores) | OpenStreetMap (Overpass API) | daily |
| Safety app | Address search and reverse lookup | Photon (OpenStreetMap geocoder), proxied by the API | cached 1 h / 24 h |
| Safety app | Walking routes along streets (foot profile) | OpenStreetMap Germany OSRM router (`Safety:Routing:BaseUrl`), proxied by the API | cached 30 min |
| Public services | City service cards (every BIP procedure) | City of Kraków Open Data API | 6 h |
| Public services | NFZ treatment waiting lists | NFZ API | 1 h per search |
| Society, services, environment | 45 city tables: residents, jobs, tourism, culture, sport events, schools, nurseries, health, parks, vehicles, lost property | City of Kraków Open Data API | 6 h |

If a source is down, its endpoints answer **503** and the rest keep working; cached data is served while a refresh fails.

The OpenStreetMap datasets (street lights, amenities, P+R, parks and other places) are large, so the API downloads them **in the background**
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
  KrakowOpenData.Web             Blazor UI (uses the client); also serves the Kompas Krakowa web app from wwwroot/safety
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

### Deployment (https://opendata.al.mt)

Every merge to `master` runs [.github/workflows/deploy.yml](.github/workflows/deploy.yml): it runs the tests, then
calls the webhook of the Dockhand Git stack that runs [docker-compose.dockhand.yml](docker-compose.dockhand.yml) on the
OCI host. Dockhand pulls the repo and builds both images there (its `--build` deploy option), and the workflow waits
until `/health` reports an API started after the webhook call. To redeploy without a merge, run the workflow by hand
from the Actions tab. To roll back, revert the commit on `master`.

### Running the tests (optional)

Not needed to run the apps. From the solution folder: `dotnet test`, or **Test → Run All Tests** in
Visual Studio. The tests replace every public source with in-memory fakes, so they need no internet.

## Kompas Krakowa app: night safety, heat, flood and air

A web app and API that combine the open data above into four scores for any place in Kraków (night safety, heat, flood safety and
clean air), and let residents and city planners act on them.

- **Heat-relief score** (0 very hot – 100 cool, **higher = better, like every score**): how well a place can cool down on a hot day, from the relief it has: shade, shade, water, a cool indoor place, toilets and a way to reach relief
- **Night-safety score** (0 unsafe – 100 safe, **higher = safer**): how well set up is this place for walking at night? (street lighting, night transport, places open at night, a defibrillator)

Both are 0–100, computed for every ~250 m square of the built-up city. They are **environmental scores built from
mapped infrastructure and resident reports, not crime statistics** (Kraków publishes no open, geolocated incident data).

**Open the app** (start the API and the Web app as described in [How to run](#how-to-run)):

| Who | Where |
| --- | --- |
| Residents | http://localhost:5090/safety/ |
| City planners | http://localhost:5090/safety/#/planner, also the **Planner dashboard** link in the site menu. It opens directly: the Web app signs in with the demo key (`Safety:PlannerDemoKey`; empty it to require the key on the sign-in screen) |

### Residents

- A map coloured by the chosen view, four tabs: **Night safety**, **Heat relief**, **Flood** and **Air**. The view is suggested from the
  conditions: a river above its warning level → flood, high PM2.5 → air, otherwise dark → night safety, hot or a heat warning → heat.
- **Each view shows only its own data.** Heat relief: the heat-relief score, its factors, where to cool down, heat reports, heat alerts and the heat situation.
  Night safety: the night-safety score, its factors, safe places open at night, night reports and alerts, and daylight. Flood: the flood-safety
  score, distance from rivers, help and exits nearby, flood reports and alerts, and the river situation. Air: the clean-air score, distance from
  main roads, trees and indoor places, air reports and alerts, and the current PM2.5.
- **Menu**: find a place by **address, street or stop** (suggestions as you type, with visible *Show: All / Addresses / Stops* filters), use your location,
  set "my area" for alerts, start a walk check. Every other screen has a labelled **Back to menu** button.
- Tap anywhere (or pick a search result) for a **place card**: the score, *why* (every factor with its distance and weight), nearby help with walking
  times, and open reports of that view.
- **Every map says what it is looking at**: the address of the place (looked up from the coordinates), the radius covered (a dashed circle for the
  1.5 km that help is searched within, an outlined square for the 250 m the score and reports apply to) and, for a walk, the start and end addresses.
- **Report a concern**, limited to the types of the chosen view (Heat relief: no shade, water point not working, overheated spot; Night safety: lamp out,
  feels unsafe, hazardous path; Flood: flooded street or underpass, blocked drain, river rising fast; Air: smoke or burning smell, strong fumes,
  dust cloud). Place it by dragging the pin, tapping the map or typing an address. Reports move the score of their square, see below.
- **Walk check** that follows the view: **Check a night walk** (Night safety: lighting, night transport, open places), **Check a cool walk**
  (Heat relief: water, shade, cool places, toilets), **Check a flood-safe path** (distance from rivers, help and exits) or **Check a clean-air path**
  (distance from main roads, trees, indoor places). Type the start and the destination with suggestions, tap the map, or use
  your area. **Safety wins over distance:** when the fastest route is poor (average goodness under 65 or its weakest stretch under 45) the search widens to
  farther via points and a detour of up to its own length again (at most 3 km more), and the app says why it is longer; when the fastest route is
  good enough only a nearby improvement is offered. **Routes follow real streets** (OpenStreetMap foot routing): the fastest route is shown with a **safer / cooler / better** alternative when one scores clearly better (at least 3 points on average, at most 30 % longer). Start (A) and destination (B) are clearly marked, the route is coloured by score along the way, the weakest spot is marked with its address and the nearest help to it.
- **Every score explains itself.** The "!" next to a score, a factor row, the map legend and "Weights and sources" open a panel with what 0 and 100 mean, the colour ranges (the same four for every score: 75–100 Good … 0–35 Critical), the weight of each factor and *why* it has that weight, the data source, how many such features are mapped in Kraków, and for this place which factors added how many points. The text comes from `GET /api/safety/method`, built from the same code as the scores. The map shows streets and, from zoom 14, the places that feed the scores (water, parks, toilets, refuges, night-open places, defibrillators).
- **Alerts** from planners for the area the resident has chosen ("my area"), shown for the current view (heat alerts in Heat, night alerts in Night safety, flood alerts in Flood, air alerts in Air,
  general alerts always). Banners while the app is open, optionally device notifications (switch on the menu).
- Languages: **Polish, English, Ukrainian** (selector in the top bar; there is no settings screen). Works on phones (bottom sheet) and wide screens
  (side panel); follows the device's light or dark mode; keyboard and screen-reader friendly.

### Planners

Sign in with the planner key; the dashboard has a global **event selector** (Heat relief / Night safety / Flood / Air quality) that decides what is ranked
**and what is shown**: with Heat selected the dashboard, drawers, map markers, report lists and conditions are about heat only, with Night safety about
night safety only, and likewise for Flood and Air. Every page except the overview has a **Back to dashboard** button. Addresses are shown for map squares, alert centres and
reports; the alert composer and the map take an address to centre on.

| Page | What it does |
| --- | --- |
| Overview | Situation banner, headline numbers, score distribution, *what is missing most* (tap a bar to filter), and **where to act first** (ranked list, with a table view for every chart) |
| Map | The city coloured by priority or score, with resident reports and active alert areas; tap a square to drill down |
| Area drawer | Opens from any list or map square: the full factor table, nearest assets, resident reports (with notes: verify or resolve them) and **suggested actions** |
| Reports | All resident reports with filters; verify, resolve, show on map, alert the area |
| Alerts | Compose an alert for **everyone currently inside a circle** (templates, translations, severity, duration, live reach estimate, two-step send); manage running alerts |
| Weights | How much each factor counts in each of the four scores. Type any numbers (each score is scaled to 100), see why every factor has its default weight, save or reset. **Applies to every score, residents included.** |
| Contacts | Agencies (ZDMK road faults and lighting, the city services portal, Crisis Management Centre, Straż Miejska, ZZM green spaces, ZTP transport), a prefilled **brief** in Polish or English, and a contact log |

From an area drawer a planner can **create an alert** for that area or **contact an agency** with a brief that states the location, scores, weak factors,
open reports and the requested action.

### How the scores are computed

The model lives in one documented file: [`SafetyModel.cs`](src/KrakowOpenData.Application/Safety/SafetyModel.cs) (with
[`ReportRules.cs`](src/KrakowOpenData.Application/Safety/ReportRules.cs) for reports). Summary:

1. The city is cut into 250 m squares ([`GridSpec`](src/KrakowOpenData.Application/Safety/GridSpec.cs)); a square is on the map when it has 3+ mapped street lamps or a stop.
2. Each **factor** is a straight-line distance to the nearest feature (or a density) turned into 0–100: full score up to a near distance, zero beyond a far one, a straight line between. Every factor adds its weight in proportion to its score, so a park within 100 m earns the full 35 cooling points and none nearby earns 0.
3. A layer's **base score** is the weighted sum of its factors (weights add up to 100); open reports change it by up to 30 points (they lower the score of their own layer). **All four scores point the same way: higher = better**, with the same four bands and colours (75–100 Good, 55–75 Fair, 35–55 Weak, 0–35 Critical). The heat score used to run the other way (0 = cool); it is now the heat-relief score so a high number, a long bar and a blue colour always mean good.

| Layer | Factor (weight) | Data | Full score → zero |
| --- | --- | --- | --- |
| Heat | Drinking water (25) | OSM drinking-water points | ≤ 150 m → 800 m |
| Heat | Parks and shade (35) | OSM parks, measured to the park's edge (no tree-canopy data) | ≤ 100 m → 600 m |
| Heat | Cool indoor place (20) | libraries, pharmacies, hospitals (opening hours not checked) | ≤ 200 m → 900 m |
| Heat | Public toilets (10) | OSM toilets | ≤ 200 m → 800 m |
| Heat | Public transport (10) | GTFS stops | ≤ 150 m → 600 m |
| Night | Street lighting (40) | OSM lamps per km² in the 3×3 squares around (not lux) | ≥ 400 /km² → ≤ 40 /km² |
| Night | Night transport (25) | stops with departures 23:00–04:30 (GTFS) | ≤ 250 m → 1000 m |
| Night | Open at night (20) | police, hospitals, pharmacies tagged `24/7` | ≤ 250 m → 1000 m |
| Night | Defibrillator (15) | OSM AEDs | ≤ 100 m → 500 m |
| Flood | Distance from rivers (55) | OSM rivers, streams, canals (sampled every 120 m) | 50 m → ≥ 500 m (farther is safer) |
| Flood | Hospital or police nearby (25) | OSM hospitals and police stations | ≤ 300 m → 1.4 km |
| Flood | Way out (20) | GTFS stops | ≤ 150 m → 600 m |
| Air | Distance from main roads (45) | OSM motorway, trunk and primary roads (sampled every 120 m) | 30 m → ≥ 300 m (farther is cleaner) |
| Air | Parks and trees (35) | OSM parks, measured to the edge | ≤ 100 m → 600 m |
| Air | Indoor place to wait out bad air (20) | libraries, pharmacies, hospitals | ≤ 200 m → 900 m |

**Flood and air are live as well as structural.** When IMGW reports a river above its warning level, places near water lose up to 35 points
(level × 35 × (1 − river score ÷ 100); 0.5 above warning, 1 above alarm). When the average GIOŚ PM2.5 is above 15 µg/m³ every place loses points,
up to 40 at 75 µg/m³ or more, and poorly protected places lose most. Both scores are "higher = better". They show *exposure*, not measurements:
there is no elevation or official flood-hazard map in the flood score yet, and traffic volume and industry are not in the air score. If the rivers
or main roads have not loaded yet the factor reads "unknown" (50) and the dashboard says so.

4. **Combined** (heat and night safety together, API only; the apps no longer have a "Both" view) = 0.6 × the lower layer + 0.4 × the average.
5. **Exposure** (0.2–1.0) estimates how many people a square affects from lamp and stop counts, because there is no open population grid.
6. **Planner priority** = (100 − score) × exposure × urgency, where urgency follows the live heat situation (none 0.8, hot day 1.0, warning level 1–3 → 1.1 / 1.25 / 1.5).

**Resident reports** (`ReportRules`): per square and type, a single unconfirmed report counts a quarter; two independent supporters (anonymous device
ids; "still true" confirmations count) or a planner verification count in full, with a boost for more supporters; the effect halves every half-life
(1 day for an overheated spot up to 14 days for a lamp out) unless confirmed again. Resolved reports count for nothing. A device may send 5 reports an hour.

All thresholds are first estimates, meant to be tuned in `SafetyModel`. Unit tests pin the worked examples.

### Address search

Address boxes and the address shown on maps use **OpenStreetMap data through [Photon](https://photon.komoot.io)**, a geocoder that supports search-as-you-type
(the public Nominatim service does not). The browser never calls Photon: it calls this API (`/api/geo/search`, `/api/geo/reverse`), which sends only the typed
text or one coordinate, caches answers (searches 1 h, lookups 24 h) and allows two requests at a time, to stay within Photon's fair use. Change the server with
`KrakowData:GeocoderBaseUrl` (for example a self-hosted Photon). Addresses seen once are remembered on the device, so they still show offline; typing a new
address needs a connection, tapping the map does not.

### Water data

Heat scores depend heavily on drinking-water points, and OpenStreetMap has few: about 100 tagged `amenity=drinking_water` for a city of ~800,000 (plus a few
water points, and taps and fountains that are only tagged drinkable sometimes). The data query now also takes `amenity=water_point`, and taps, fountains and
springs explicitly tagged `drinking_water=yes`, and drops anything tagged `drinking_water=no`. It still cannot know about fountains nobody has mapped, so the
app says so: the place card shows how few points are mapped when water is the weak factor, and the planner overview carries a data note. Improving this means adding
points in OpenStreetMap or getting a list from the water utility or the city. The new query applies at the next daily download (delete
`%LOCALAPPDATA%\KrakowOpenData\cache\osm-amenities.json` while online to force it).

### Alerts and privacy

Planners create an alert for a circle (centre, radius 100 m–5 km, severity, message, optional PL/UK translations, expiry). The resident app asks
`GET /api/safety/alerts?lat&lon` about once a minute while it is open and online; the server keeps **no recipient list**. To show planners an estimate of reach
it remembers, **in memory only**, a random device id with the last position for 15 minutes. Position is sent only for users who set "my area". Reports
carry only the random device id, never an account. Real push notifications (when the app is closed) would need a push service and are not implemented.

### Works offline

- The app shell is a service worker cache, so it opens with no connection after the first visit (Leaflet is loaded from cdnjs and cached by the same worker).
- API answers are saved in IndexedDB (the last map, conditions, place cards, nearby-help list, alerts). With no connection, the map still shows saved scores,
  place cards fall back to the saved card or to the saved grid plus the saved nearby list, the night walk check is estimated from the saved grid, and the status pill says what is shown and how old it is.
- **Reports written offline are queued on the device** and sent automatically when the connection returns. The planner dashboard shows its last saved data read-only; actions that change data are disabled until online.
- Map tiles you have looked at are cached (up to 400); without tiles the score squares still draw on a grey background.

### API

All under `/api/safety` (see Swagger for schemas). Planner endpoints need the `X-Planner-Key` header.

| Endpoint | Purpose |
| --- | --- |
| `GET conditions` | Heat pressure, air, rivers, warnings, daylight, suggested view |
| `GET grid?event=heat\|night\|both` | The whole score grid, compact (for the map and offline use) |
| `GET place?lat&lon&event` | Scores, factors, nearest help, nearby reports, suggested actions for any point |
| `GET cells/{id}?event` | The same for a grid square (planners also get report notes) |
| `GET route?from&to&mode=night|heat|both` | **Walking routes along streets**: the fastest and, when clearly better, a safer / cooler one, each scored every 50 m |
| `GET method` | How every score is built: meaning of 0 and 100, bands, each factor with weight, thresholds, reason for the weight, data source and mapped count, definitions of the dashboard figures |
| `GET corridor?from&to` | Scores sampled every 50 m along the straight line between two points (kept for simple clients) |
| `GET features` | Water points, parks, refuges, … for offline "nearest" |
| `GET /api/geo/search?q&lat&lon`, `GET /api/geo/reverse?lat&lon` | Address search with autocomplete, and the address of a point (OpenStreetMap via Photon) |
| `GET report-types`, `GET reports`, `POST reports`, `POST reports/{id}/confirm` | Citizen reports |
| `GET alerts?lat&lon&deviceId` | Active alerts covering a point |
| `GET planner/summary?event` | Dashboard numbers: KPIs, histogram, factor gaps, ranked places |
| `POST planner/reports/{id}/verify\|resolve` | Review reports |
| `GET\|POST planner/alerts`, `DELETE planner/alerts/{id}`, `GET planner/reach` | Alerts and audience estimate |
| `GET planner/agencies`, `GET\|POST planner/dispatches` | Agency contacts |
| `POST planner/demo-data` | Adds sample reports in the lowest-scoring areas (for demos) |

### Configuration

```json
// KrakowOpenData.Api/appsettings.json
"Safety": {
  "PlannerKey": "demo-planner",   // CHANGE THIS. Shared key: demo-grade, not real authentication
  "Persist": true,                // keep reports, alerts and contacts in a JSON file across restarts
  "StorePath": ""                 // default: %LOCALAPPDATA%\KrakowOpenData\safety-store.json
}
// KrakowOpenData.Web/appsettings.json
"Safety": { "PublicApiBaseUrl": "http://localhost:5080/",    // the API address as the browser sees it (docker-compose sets it)
            "PlannerDemoKey": "demo-planner" }             // demo only: planner opens without sign-in. EMPTY IT outside demos
// KrakowOpenData.Api/appsettings.json, optional
"Safety": { "Routing": { "BaseUrl": "https://routing.openstreetmap.de/routed-foot/" } }   // OSRM-compatible foot router
```

### Where things are

```
src/KrakowOpenData.Application/Safety/   scoring model, grid, reports, alerts, planner summary, agencies (all documented)
src/KrakowOpenData.Infrastructure/Safety/ JSON-file store, simulated agency gateway;  OpenStreetMap/SafetyPlaces*  (parks, libraries, …)
src/KrakowOpenData.Api/Endpoints/SafetyEndpoints.cs
src/KrakowOpenData.Web/wwwroot/safety/   the web app: index.html, sw.js (offline), manifest, css/, js/ (no build step)
  js/resident.js report.js walk.js alerts.js      resident view
  js/planner*.js charts.js                        planner dashboard
  js/api.js db.js chrome.js                       API client + offline cache, IndexedDB, outbox
  js/strings-*.js                                 Polish / English / Ukrainian texts
```

### Known limits (read before relying on it)

- **No crime data.** Police maps and statistics for Kraków are not open data; the scores say so on screen. Citizen reports are the only incident signal.
- OpenStreetMap can miss lamps, fountains and pharmacy hours; scores reflect what is mapped. Lighting is lamp density, not measured light.
- Population is not known per square; **exposure** is a proxy from lamps and stops. The district table has no boundaries to improve it.
- **Agency contact is simulated**: it records a reference and prefilled text; nothing is sent. The two phone numbers (ZDMK 24 h line, Crisis Management Centre) come from press coverage and are flagged "verify number".
- The planner key is a shared demo secret; use the city's identity provider before real use. The JSON store suits a demo, not production.
- The service worker could not be exercised in the in-app browser used for development (it does not support service workers); the data-level offline behaviour (cache, queue, auto-send) was tested there, the installable shell should be checked in Chrome or Edge.
- Street routing uses the public OpenStreetMap Germany foot router (`Safety:Routing:BaseUrl`), which is fair-use with no guarantee; self-host OSRM or Valhalla for production. If it is unreachable the API returns only a straight-line check and the app says so. Routes are scored with the same model as the map, so a "safer" route means better lit and better served, not a crime-checked one.
- **Address search depends on a third-party service** (Photon, free and fair-use). Typed text and coordinates are sent to it by this API, not by the browser; self-host it for production.
- Water points are sparse in OpenStreetMap (see [Water data](#water-data)); low heat scores partly reflect missing map data.

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
