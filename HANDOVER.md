# Handover: Kompas Krakowa (HackYeah 2026)

For a new session picking this up. Written 2026-10-03. The README has the user-facing docs; this file is the working state: what exists,
which branch has what, decisions, and what is left.

## What this is

**Kompas Krakowa** (Kraków Compass; the code and URL still say "safety": `/api/safety/*`, `/safety/`) is an installable, offline-capable web app
plus open API on top of the Kraków Open Data solution (.NET 8). Goal: a one-stop open data library for Kraków; the MVP proves it on
**heat** and **night safety** for residents and city planners.

- **Residents**: map in 250 m squares coloured by **Night safety**, **Heat** or **Both**; place cards with reasons; "?" explanations of every score;
  fastest vs. safer/cooler **street routes** (OSM foot routing); address search; reports with neighbour confirmation; alerts for "my area"; offline use.
- **Planners**: dashboard (overview, map, reports, alerts, contacts), event selector, area drill-down, every figure clickable (how it is computed + source),
  alerts to a map circle, simulated agency contact.
- **Scores are environmental, not crime statistics.** Heat score: **higher = hotter** (0 cool, 100 very hot; internally 100 − "cooling capacity").
  Night safety: **higher = safer**. Overall ("Both"): higher = better. Heat weights: shade 35, water 25, cool indoor place 20, toilets 10, transit 10.
  Safety: lighting 40, night transport 25, open-at-night places 20, defibrillator 15. All in `SafetyModel.cs` (documented).

## Branches (nothing is merged yet except the first)

| Branch | Based on | What it has | State |
| --- | --- | --- | --- |
| `master` | | the whole safety feature with the **plain-JavaScript** front end (`wwwroot/safety/js`) | merged from `feature/safety-concerns` (PR #1) |
| `fix/planner-navigation` (`43ed635`) | master | **menu link fix** (`target="_top"` on the two `/safety/` links in `NavMenu.razor` + `SafetyLinkTests`), a **Home page link** in the plain-JS top bar, **bundled OSM seed snapshots** (below), README note | pushed, not merged. **Current branch** |
| `react-frontend` (`a3e42a2`) | master | the front end **rebuilt in React + TypeScript** (`frontend/`), 142 front-end tests, planner "no data" handling, README for running with `npm run dev` | pushed, not merged. Does **not** have the three items from `fix/planner-navigation` |

**Next steps, in order:** (1) merge `fix/planner-navigation` into master (open a PR); (2) merge master into `react-frontend`;
(3) add the **Home page link to the React top bar** (`frontend/src/components/chrome.tsx`, `Topbar`: an `<a href="/" target="_top">` like the plain-JS one, text key `nav.home`
in `i18n/strings-resident-v2.ts`) and a test; (4) rebuild (`npm run build`) and commit the generated files; (5) merge `react-frontend` into master.
`SUBMISSION.txt` (hackathon text) is **deliberately untracked**: never commit it unless asked.

## Two fixes made this session (both on `fix/planner-navigation`)

1. **"Not found" when opening the planner from the home page.** The Blazor router intercepted same-origin links to `/safety/index.html#/planner` and rendered its
   NotFound page. Links into the safety app must carry `target="_top"` (guarded by `tests/KrakowOpenData.Web.Tests/SafetyLinkTests.cs`).
2. **Worked only on the author's machine.** The OSM datasets (street lamps, amenities, parks/pharmacies/hospitals/police) came from a per-machine cache
   (`%LOCALAPPDATA%\KrakowOpenData\cache`), so a clean machine had to download them from Overpass first (503 / "no data" meanwhile, or never if Overpass was blocked).
   Snapshots are now **embedded in the assembly** (`src/KrakowOpenData.Infrastructure/Seed/*.json`, `Common/SeedSnapshots.cs`); `FileSnapshotStore` falls back to them when
   nothing is saved, dated 2000-01-01 so a real refresh starts at once. Verified with an empty cache and an unreachable Overpass: the planner summary loads in ~6 s with the same
   2,720 squares and counts. To refresh the snapshots: copy the three files from the cache folder into `Seed/` and commit.
   Still needed on a new machine: .NET 8 SDK (`global.json` rolls forward), Node 20+ for the React front end, and internet for the live sources (ZTP GTFS timetables, IMGW, GIOŚ,
   Photon address search, OSM foot routing). The GTFS timetables are **not** bundled (downloaded on every start, memory only).

## Run it

Plain-JS front end (master / `fix/planner-navigation`): `dotnet run --project src/KrakowOpenData.Api` and `dotnet run --project src/KrakowOpenData.Web` → http://localhost:5090/safety/ ,
planner `#/planner`. React front end (`react-frontend`): API as above, then `cd frontend && npm install && npm run dev` → http://localhost:5173/ (the Vite dev server answers `config.js`
with the demo planner key; `appsettings.Development.json` on that branch allows the 5173 origin). `npm run build` writes the production build into `src/KrakowOpenData.Web/wwwroot/safety`
(committed, so the .NET apps and Docker work without Node).

- Planner key `demo-planner` (`Safety:PlannerKey` on the API). The Web app hands it to the front end as `Safety:PlannerDemoKey` (empty it to require sign-in). Demo reports: planner → Reports → "Add demo reports".
- Tests: `dotnet test KrakowOpenData.sln` (all green: Domain 69, Application 145, Api 64, Web 769, Infrastructure 90 = 1,137). On `react-frontend`: `cd frontend && npm test` (142), `npm run typecheck`.
- Stop the API and Web app before running `dotnet test` or rebuilding (running apps lock the DLLs). The user often runs the API from **Visual Studio**, which also locks them.

## Architecture and where things are

| Area | Location |
| --- | --- |
| Scoring model (formulas, weights, bands) | `src/KrakowOpenData.Application/Safety/SafetyModel.cs`, `ReportRules.cs`, `GridSpec.cs`, `SolarTime.cs` |
| Services | `Application/Safety/`: `ScoreService` (grid, place, path sampling), `RouteService` (+`PathSampler`), `MethodService` (documents weights/sources for the UI), `PlannerService`, `ConditionsService`, `ReportService`, `AlertService`, `AgencyService`, `DemoDataService` |
| Endpoints | `Api/Endpoints/SafetyEndpoints.cs` (`/api/safety/*`; planner ones need `X-Planner-Key`), `GeoEndpoints.cs` (`/api/geo/search`, `/reverse`) |
| Infrastructure | `Infrastructure/Safety/` (JSON-file store, simulated agency gateway), `OpenStreetMap/Safety*`, `Geocoding/PhotonGeocoder.cs`, `Geocoding/OsrmWalkingRouter.cs` (default `routing.openstreetmap.de/routed-foot`), `Common/SeedSnapshots.cs` |
| Plain-JS front end (master) | `src/KrakowOpenData.Web/wwwroot/safety/` (`js/`, `css/app.css`, `sw.js`; no build step) |
| React front end (`react-frontend`) | `frontend/src`: `lib/` (api, store, i18n, model, outbox, route), `resident/`, `planner/`, `components/` (ui, chrome, explain), `map/` (Leaflet layers as components), `i18n/` (texts, three languages) |
| Web host | `KrakowOpenData.Web/Program.cs`: `/safety/config.js` (API address + demo key), `/safety` and `/safety/planner` redirects, no-cache for `/safety` files; Blazor dataset pages and the menu (`NavMenu.razor`) |

Behaviours to preserve: offline (API answers cached in IndexedDB, reports queued in an outbox); the heat score direction (use `kind = 'heat'` for bands/colours); dialogs/toasts open from anywhere;
the explanation text comes from `/api/safety/method`; `softRender()`-style rule in the plain-JS app (do not rebuild a panel while an input has focus).

## Decisions and why

- Real public data only (OSM, GTFS, IMGW, GIOŚ); only demo reports/alerts, the simulated agency contact and tests are sample data. Agency phone numbers came from press coverage and are flagged "verify number".
- Heat and Night safety views show only their own data; "Both" shows them side by side.
- Photon (not Nominatim) for address search, proxied through the API. The city's `kontakt.krakow.pl` portal is linked, not competed with.
- The Blazor Web project stays: it hosts the older dataset pages and the menu; it is **not** the safety front end on `react-frontend` (that is `frontend/`).
- Name: Kompas Krakowa. Submission text (`SUBMISSION.txt`) uses Kraków-only, 2022+ evidence and positions the app as a one-stop open data library; MVP backlog there is small.

## Outstanding (MVP scope first)

1. The merges listed under "Next steps", and the React Home link.
2. MVP items from the README: a "possibly missing data" flag on the dashboard (zero score + few features mapped nearby), a simple "add a missing place" form for planners, a Polish text pass + demo data.
3. The planner **Map** page shows only a generic error when the grid is not ready (503); the Overview already explains and retries.
4. Not verified: the service worker (the in-app browser cannot run one; needs Chrome/Edge), real phone touch/GPS and dragging the bottom sheet, Polish/Ukrainian wording of newer strings (Ukrainian falls back to English in places; the long method text from the API is English only).
5. Score thresholds are first estimates (about half of built-up squares are "Critical" for safety; ~30 % score 90–100 on heat mostly because only ~75 drinking-water points are mapped in OSM).
6. Future (goal, not MVP): accident/crime layers from residents and agencies, crisis use (floods, air, outages), real agency integration and sign-in, push notifications when the app is closed.

## Environment notes (Windows)

- Node 24 was installed with winget for the React work; open a new shell to get it on PATH (in the Bash tool use PowerShell for `npm`).
- Bash heredocs with quotes often failed: use the Write/Edit tools. **PowerShell 5.1 `Get-Content | Set-Content` corrupts non-ASCII** (it mangled `×`, `−`, `·`): never use it on source files.
- The in-app browser screenshots are flaky; verify with JavaScript (`javascript_tool`) and reading the DOM instead. Background tabs report width 0: front the tab first.
- Line-ending warnings (LF→CRLF) on commit are harmless.
