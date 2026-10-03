# Handover: Safety Concerns (HackYeah 2026)

Written for a new session picking this up. Branch: `feature/safety-concerns`. **Nothing is committed yet** (`git status` shows the whole feature as modified and untracked files).
The README has a full "Safety Concerns" section; this file is the working state, decisions and what is left.

## What this is

An installable, offline-capable web app plus API on top of the existing Kraków Open Data solution:

- **Residents**: map coloured by **Night safety**, **Heat** or **Both** (0–100 scores per 250 m square), place cards with reasons, address search with autocomplete, report a concern, a walk check (night walk / cool walk), planner alerts for "my area".
- **Planners**: dashboard (overview, map, reports, alerts, contacts) with an event selector (Heat / Night safety / Both), area drill-down, create alerts for a circle, contact agencies (simulated).
- Heat and Night safety views show **only their own data** (decided with the user); Both shows the two side by side.
- Scores are **environmental (mapped infrastructure + citizen reports), not crime statistics**. Kraków has no open geolocated incident data; SEWiK road-accident data was considered and left out (its download is hosted on an unverified third-party site).

## Run it

```bash
dotnet run --project src/KrakowOpenData.Api      # http://localhost:5080  (first start 1-2 min: timetables + OSM)
dotnet run --project src/KrakowOpenData.Web      # http://localhost:5090
```

- Residents: http://localhost:5090/safety/  · Planners: http://localhost:5090/safety/planner (key `demo-planner`)
- Planner key = `Safety:PlannerKey` (API appsettings), default `demo-planner`. Demo data: planner → Reports → "Add demo reports".
- Rebuild/stop both apps before building tests (running apps lock DLLs). Tests: `dotnet test KrakowOpenData.sln` (all green at last run: Domain 69, Application 128, Api 62, Web 767, Infrastructure 82).
- Environment notes: no Node/Python on the machine, so JS has no linter or unit tests; the in-app browser cannot run **service workers**. The OSM primary Overpass server was unreachable from this machine (mirror `maps.mail.ru` worked for small queries).
- Bash heredocs containing quotes failed repeatedly in this environment; use the Write/Edit tools for files.

## Architecture and where things are

| Area | Location |
| --- | --- |
| Scoring model (all formulas, documented) | `src/KrakowOpenData.Application/Safety/SafetyModel.cs`, `ReportRules.cs`, `GridSpec.cs`, `SolarTime.cs` |
| Services | `Application/Safety/`: `SafetyModelProvider` (cached static model), `ScoreService` (grid, place, corridor, features), `ConditionsService`, `ReportService`, `AlertService` (+`PresenceTracker`), `AgencyService`, `PlannerService`, `DemoDataService` |
| Domain records | `src/KrakowOpenData.Domain/Safety/` |
| DTOs | `src/KrakowOpenData.Contracts/SafetyDtos.cs` (includes `GeocodeResultDto`) |
| Endpoints | `src/KrakowOpenData.Api/Endpoints/SafetyEndpoints.cs` (`/api/safety/*`, planner endpoints need `X-Planner-Key`), `GeoEndpoints.cs` (`/api/geo/search`, `/api/geo/reverse`) |
| Infrastructure | `Infrastructure/Safety/` (JSON-file store, simulated agency gateway), `OpenStreetMap/SafetyPlaces*` (parks, libraries, pharmacies, hospitals, police), `Geocoding/PhotonGeocoder.cs`, night-service stops in `Repositories/GtfsScheduleRepository.cs` |
| Front end (no build step) | `src/KrakowOpenData.Web/wwwroot/safety/`: `index.html`, `sw.js`, `manifest.webmanifest`, `css/app.css`, `js/` |
| Web host bits | `KrakowOpenData.Web/Program.cs`: `/safety/config.js` (API address for the browser), `/safety` and `/safety/planner` redirects, no-cache for `/safety` files; nav links in `NavMenu.razor` |

Front-end modules (`wwwroot/safety/js`): `main.js` (router), `resident.js`, `report.js`, `walk.js`, `alerts.js`, `search.js` (autocomplete), `geo.js` (addresses), `map.js` (Leaflet from cdnjs, grid canvas, map info/coverage), `api.js` (client + offline cache), `db.js` (IndexedDB), `chrome.js` (top bar, status pill, outbox, how-it-works), `planner*.js`, `charts.js`, `i18n.js` + `strings-resident.js`, `strings-resident-v2.js` (later keys override earlier ones), `strings-planner.js`.

Key behaviours to preserve:
- Offline: API answers cached in IndexedDB (`cachedGet`), reports queued in an outbox and sent on reconnect, saved grid/features give place cards and walk estimates offline.
- `softRender()` in `resident.js` must not rebuild the panel while an input has focus (a bug that wiped typed search text).
- `sw.js` lists the files to precache; **add new JS files there**.
- Resident layers: `layersOf(mode)`; reports/alerts/conditions/nearby help are filtered per mode; planner uses `P.event`.
- Priority = (100 − score) × exposure × urgency; shares in the dashboard are exposure-weighted. Exposure is a proxy (lamps + stops) because there is no population grid.

## Decisions and why

- Data sources are all real public data (OSM, GTFS, IMGW, GIOŚ). Only demo reports, alerts, the simulated agency contact and tests are sample/simulated.
- Address search uses **Photon** (supports autocomplete; Nominatim does not), proxied through the API so the browser never talks to it.
- Agency contact is **simulated** (records a `SIM-…` reference). Phone numbers for ZDMK (12 616 75 55) and the Crisis Management Centre (12 616 59 99) came from press coverage and are flagged "verify number".
- Planner key is a demo shared secret; store is a JSON file (`%LOCALAPPDATA%\KrakowOpenData\safety-store.json`, contains demo reports and a test alert from development).
- The existing `kontakt.krakow.pl` city report portal exists; the app deep-links to it for faults rather than competing.

## Outstanding

1. **Commit** (user has not asked yet). Suggested: one commit on `feature/safety-concerns`; add the attribution line from the session instructions.
2. **Not verified**: the offline app shell/service worker (needs Chrome/Edge), real phone touch and GPS, and a re-check of the mobile layout and the planner alert composer's new address search after the last round of changes.
3. **Water data**: the broadened OSM query only applies at the next daily download; to force it delete `%LOCALAPPDATA%\KrakowOpenData\cache\osm-amenities.json` while online. Only ~100 drinking-water points exist in OSM for Kraków, so many areas score low partly from missing data (the UI says so). A list from the water utility or the city would help.
4. **Score thresholds** (`SafetyModel.cs`) are first estimates; about half of built-up squares come out "Critical". Needs a sanity check by someone who knows Kraków.
5. **Not built**: push notifications when the app is closed, street routing for walks, real agency integration and authentication, crime/incident data, a native-speaker review of the Polish/Ukrainian strings, Ukrainian translations for the planner screens (they fall back to English).
6. Possible cleanups: old unused translation keys in `strings-resident.js` (settings screen, old walk texts); `chrome.js` has some unused imports; areas show "Near <stop>" or an address, not a district name.
7. A `.slnLaunch`/launch profile for the Web app does not need changing; docker-compose already passes `Safety__PublicApiBaseUrl`.

## Quick checks for a new session

```bash
git branch --show-current                      # feature/safety-concerns
dotnet test KrakowOpenData.sln                 # all green
curl localhost:5080/api/safety/conditions      # API up
curl localhost:5090/safety/config.js           # new Web build running (shows window.KRK_CONFIG)
```

## Update: routes, meaningful scores, explanations (2026-10-03)

Done in this session (all tests green: Domain 69, Application 145, Api 64, Web 767, Infrastructure 84):

- **Heat score now means heat**: 0 = cool, 100 = very hot (`SafetyModel.HeatScore`). Internally the model still measures *cooling capacity* (higher = better); `ScoredCell.Cooling`/`Goodness(evt)` give the "higher = better" view used for bands, combined score and priority. Heat weights: green 35, water 25, refuge 20, toilets 10, transit 10. In JS, pass `kind = 'heat'` to `bandOf`/`histogram`/`gauge` (see `model.js`).
- **Street routes**: `GET /api/safety/route?from&to&mode` (`RouteService`, `IWalkingRouter` → `OsrmWalkingRouter`, default `routing.openstreetmap.de/routed-foot`). Fastest + a safer/cooler/best alternative (needs ≥ 3 points average gain, ≤ 30 % longer). Falls back to a straight line when routing is down. UI in `walk.js` (A/B markers, route coloured by band, fastest dashed grey).
- **Explanations**: `GET /api/safety/method` (`MethodService`) + `js/explain.js` (score/factor/KPI dialogs, legend body). Weights, reasons, sources and mapped counts come from the API.
- **Map**: grid is more transparent so street names show; `PlacesLayer` draws amenities from zoom 14 (button on the resident map, checkbox on the planner map).
- **Planner**: every figure is clickable (KPI tiles, histogram bars, gap bars via "?", conditions chips, report-type bars, drawer gauges and factor table). The menu link opens the dashboard directly: Web `Safety:PlannerDemoKey` is exposed through `/safety/config.js` as `plannerAutoKey` and `planner.js` signs in with it (empty it to require the key).
- New files: `Application/Safety/{RouteService,MethodService,PathSampler}.cs`, `Application/Abstractions/IWalkingRouter.cs`, `Infrastructure/Geocoding/OsrmWalkingRouter.cs`, `Infrastructure/Options/RoutingOptions.cs`, `wwwroot/safety/js/{explain,strings-resident-v3,strings-planner-v2}.js`.

Not verified: mobile layout of the new route cards, legend and dialogs; Polish/Ukrainian wording of the new strings (Ukrainian falls back to English in places; the long method text from the API is English only); the service worker (needs Chrome/Edge; bumped to cache `v2`).
Known limit: about 30 % of squares score 90–100 on heat mostly because only ~75 drinking-water points are mapped.
