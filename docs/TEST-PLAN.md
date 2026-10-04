# Kompas Krakowa: test plan

Manual test plan for the whole product: the home page, the open data catalog and its dataset pages, the resident app, the planner dashboard, the API,
and the cross-cutting qualities (usability, responsiveness, accessibility, languages, offline, security). Every case has steps and an **expected result**.
Automated tests are listed in section 14 and should pass before manual testing starts.

## 1. How to use this plan

| Item | Detail |
| --- | --- |
| Result column | Mark each case **P** (pass), **F** (fail, add a defect id) or **B** (blocked) while testing |
| Priority | **H** must pass for a release or demo, **M** should pass, **L** nice to have |
| Case ids | `HOME` home page, `CAT` catalog and dataset pages, `RES` resident app, `REP` report flow, `PLN` planner, `E2E` resident to planner, `API`, `UX`, `RSP` responsive, `A11Y` accessibility, `I18N` languages, `OFF` offline, `SEC` security, `PERF` performance |
| Defects | Note the case id, language, device or browser, screenshot, and what you expected |

## 2. Scope

**In scope:** everything served by `KrakowOpenData.Web` (Blazor pages and the `/safety/` app) and the REST API under `/api`.

**Out of scope:** the accuracy of the external sources themselves (ZTP, IMGW, GIOŚ, OpenStreetMap), the Docker/CI pipeline, load testing beyond section 13.

**Known behaviours that are not defects:**

- Scores are **environmental** (mapped infrastructure plus resident reports), not crime statistics.
- **Every score reads the same way: higher = better** (heat relief is a score: 100 = cool, 0 = very hot). Colours always show good in blue and bad in red, with the same four bands for every score.
- The planner key is a shared demo key (`demo-planner`); the planner opens without sign-in when `Safety:PlannerDemoKey` is set.
- Agency contact is **simulated**: it records a contact but sends nothing.
- The first API start takes 1–2 minutes while timetables and OpenStreetMap data load. Until then some endpoints answer 503 or "still loading".
- There is **no "Both" view**: residents choose *Night safety*, *Heat relief*, *Flood* or *Air*; planners choose *Heat relief*, *Night safety*, *Flood* or *Air quality*.
- **Flood and air are exposure scores, not measurements.** The flood score has no elevation or official flood-hazard map; the air score does not know traffic volume or industry. Both are "higher = better" and are adjusted by live IMGW river levels and GIOŚ PM2.5.
- The first start downloads rivers and main roads from OpenStreetMap; until they load, the river and traffic factors read "unknown" (50) and the dashboard says datasets are still loading.

## 3. Test environment

### 3.1 Start the system

```bash
dotnet run --project src/KrakowOpenData.Api
dotnet run --project src/KrakowOpenData.Web
```

| What | Address |
| --- | --- |
| Home page | http://localhost:5090/ |
| Available Open Data (catalog) | http://localhost:5090/catalog |
| Resident app | http://localhost:5090/safety/index.html |
| Planner dashboard | http://localhost:5090/safety/index.html#/planner |
| API documentation (Swagger) | http://localhost:5080/swagger |

Stop the API and Web app before running `dotnet test` or rebuilding; running apps lock the DLLs.

### 3.2 Devices and browsers

| Group | Minimum coverage |
| --- | --- |
| Desktop | Chrome (latest), Edge (latest), Firefox (latest) at 1280 px and wider |
| Phone | Chrome on Android and Safari on iOS, portrait and landscape; or browser emulation at 320, 360, 375, 390 and 412 px wide |
| Tablet | 768 px and 1024 px |
| Colour scheme | Light and dark (the `/safety/` app follows the device setting) |

### 3.3 Test data and accounts

| Need | How |
| --- | --- |
| Two different residents | Two browser profiles, or one normal and one private window. Each browser has its own anonymous device id, which matters for confirmations and the 5-reports-an-hour limit |
| Planner | Open the planner link (demo key) or sign in with `demo-planner` |
| Demo reports | Planner, **Reports**, **Add demo reports** |
| Clean state | Delete the store file `%LOCALAPPDATA%\KrakowOpenData\safety-store.json` with the API stopped, to start without reports and alerts |
| Reset a browser | DevTools, Application, **Clear site data** (also unregisters the service worker) |

### 3.4 Entry and exit criteria

- **Entry:** the API and Web app start without errors, the Swagger page loads, automated tests pass.
- **Exit:** all **H** cases pass, no open defect of severity critical or high, every **M** failure has an owner and a decision.

---

## 4. Home page (`HOME`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| HOME-01 | H | Home layout | Open `/` | A dark hero with the shield mark behind the title **Kompas Krakowa**. Exactly **three** tiles: **Available Open Data**, **Resident mode**, **Planner dashboard**. Each has an icon, a title, a one-sentence description and an **Open** button. No other dataset cards | |
| HOME-02 | H | Open Available Open Data | Click the first tile | Opens `/catalog` (Available Open Data) in the same tab. A **← Home** link is at the top | |
| HOME-03 | H | Open Resident mode | Click the second tile | The resident app loads at `/safety/index.html` as a full page (not a "Not found" screen) | |
| HOME-04 | H | Open Planner dashboard | Click the third tile | The planner dashboard loads at `/safety/index.html#/planner`, no "Not found" screen | |
| HOME-05 | H | Back to home from every view | From the catalog use **← Home**; from the resident app and from the planner use **Home page** in the top bar | Each returns to `/` and shows the three tiles | |
| HOME-06 | M | Tile interaction | Hover and keyboard-focus each tile | Hover lifts the tile and shows a cyan outline; focus shows a visible outline; Enter activates the link | |
| HOME-07 | M | Sidebar | Look at the sidebar | Items: **Home**, **Available Open Data**, group **Tools** (Heat and night safety, Planner dashboard), then Mobility, Environment, Climate & crisis, Urban space, Public services, Society. The current page is highlighted | |
| HOME-08 | M | Sidebar links to the safety app | Click **Heat and night safety** and **Planner dashboard** in the sidebar | Both load the safety app pages directly, never the Blazor "Not found" page | |
| HOME-09 | M | Browser tab | Look at the tab | Title **Home · Kompas Krakowa**, the shield favicon (no "K" tile) | |
| HOME-10 | L | Unknown address | Open `/does-not-exist` | A "Not found" message with **Back to the home page** that returns to `/` | |
| HOME-11 | M | Sidebar "Features" | Look at the sidebar | The group is called **Features**; its two items stand out from ordinary menu items (cyan tint, border, icon); the **Planner dashboard** item is amber with a lock and a **City staff only** badge, clearly different from the resident item | |
| HOME-12 | M | Planner tile | Look at the three home tiles | The Planner dashboard tile has an amber border and a **City staff only** badge; its text says it is for city staff, not for everyone | |

## 5. Available Open Data catalog and dataset pages (`CAT`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| CAT-01 | H | Catalog content | Open `/catalog` | Heading, a colour legend (live feed, public API, file, not wired yet) and category sections (Mobility, Environment, Climate & crisis, Urban space, Public services, Society). Each dataset card shows an access badge, format, title, notes, publisher link and, if wired, an **Open** button and `GET /api/...` route | |
| CAT-02 | H | Open a dataset | Click **Open** on a card | The matching dataset page opens (e.g. *Public transport stops* opens Stops) | |
| CAT-03 | M | Publisher links | Click a publisher name | Opens the source in a **new tab**; the catalog stays open | |
| CAT-04 | M | API down | Stop the API, reload `/catalog` | A red message "could not load" with a **Retry** button; no crash. After restarting the API, **Retry** loads the catalog | |
| CAT-05 | M | Loading state | Reload the catalog | A spinner and "Loading" text show until data arrives | |

Run **CAT-10 to CAT-25** for each dataset page. For every page also check: the title, category and source line are shown; the loading, error (with **Retry**) and empty states work; the language switch translates the page chrome (not city-published data).

| Id | Pri | Page | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| CAT-10 | H | Stops (`/mobility/stops`) | Search "Rondo Mogilskie"; use **Near this point** with the default coordinates; open **Departures** on a result | Search lists matching stops with name, id, feed, step-free and a count. Near-point mode shows distances in metres. Departures page lists upcoming departures with line, headsign, time and delay | |
| CAT-11 | M | Stops: invalid coordinates | Type `abc` in latitude, click **Near this point** | An error message about the coordinates; no request is sent | |
| CAT-12 | M | Stops: paging | Clear the search, click **Next** and **Previous** | Page number and total change; **Previous** is disabled on page 1 and **Next** on the last page | |
| CAT-13 | M | Live vehicles (`/mobility/vehicles`) | Open; filter by a line; click **All vehicles** | A map of vehicles with a legend (tram, bus, other) and a table. The filter narrows both; **All vehicles** clears it | |
| CAT-14 | M | Disruptions (`/mobility/alerts`) | Open | A list of service alerts, or an empty message when there are none | |
| CAT-15 | M | P+R car parks (`/mobility/park-and-ride`) | Open | Car parks with capacity, EV spaces and hours | |
| CAT-16 | M | Weather (`/environment/weather`) | Open | Cards per station with temperature (°C), wind, humidity, pressure and observation time | |
| CAT-17 | M | Air quality (`/environment/air-quality`) | Open | Stations with PM2.5, PM10, NO₂ and the Polish index label | |
| CAT-18 | M | River levels (`/crisis/rivers`) | Open | Gauges with level, warning and alarm thresholds and a status | |
| CAT-19 | M | Warnings (`/crisis/warnings`) | Open | Current IMGW warnings with level and validity, or an empty message | |
| CAT-20 | M | Districts (`/urban/districts`) | Open | Districts with population | |
| CAT-21 | M | Amenities (`/urban/amenities`) | Pick each type; use a point filter | Lists defibrillators, **drinking fountains and taps**, public toilets, EV chargers and bike parking. The type names are the clear labels, not "drinking water" | |
| CAT-22 | M | Street lights (`/urban/street-lights`) | Open | Counts and a sample, with the source named | |
| CAT-23 | M | Service cards (`/services/cards`) | Search a procedure; expand a card | Cards show steps, facts and links | |
| CAT-24 | M | NFZ waiting lists (`/services/waiting-lists`) | Click an example chip; search a benefit | Results table with the shortest waits; hint shown before the first search | |
| CAT-25 | M | City Open Data tables (`/open-data`) | Pick a table | Columns and rows appear as published; many-column tables stay usable on a phone (see RSP) | |
| CAT-30 | H | Vehicle line filter | Live vehicles: type a line number a vehicle is running (for example 664), then `T:52`-style ids, `A664`, `a 664`; click **All vehicles** | The count and table show only that line for every spelling (case and spaces ignored); a wrong mode letter or a line that is not running shows 0; **All vehicles** restores everything; Enter submits the filter; the 20 s refresh keeps the filter | |
| CAT-31 | H | Amenity type and radius | Urban amenities: change the type; change the radius (300, then 10 or 99999); clear the point | Changing the type reloads at once; results are within the radius and nearest first; a radius outside 50–10,000 m is refused with a message and old results are not left on screen; without a point there are no distances | |
| CAT-32 | H | NFZ examples and urgent | Waiting lists: click each example chip; tick and untick **urgent**; type 2 letters | Every chip returns providers; ticking **urgent** re-runs the search at once and the list matches the box; fewer than 3 letters shows a message, not silence | |
| CAT-33 | M | Stop search forms | Stops: search by name, word order swapped, without diacritics, by code; use **Near this point** with bad and good coordinates; page through | Matches ignore case, diacritics and word order; bad coordinates show a message; paging is consistent with the filter | |
| CAT-34 | M | Table filter | Open Data table: type a word that matches nothing | A "no row matches the filter" message with a way to clear it | |
| CAT-35 | M | API filters | Call the filters in Swagger: stops, nearby, routes?mode, vehicles?routeId, amenities?kind and near, street-lights?technology, warnings?teryt, tables?category, safety reports (layer, type, status, verified, q) | Valid filters narrow the result; unknown kinds, layers, types, categories and statuses return 400 with the allowed values; every filter is applied before any limit | |

---

## 6. Resident app (`RES`)

Open `/safety/index.html`. Use a fresh browser profile for the first run so the welcome screen shows.

### 6.1 First run, map and views

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RES-01 | H | First load | Open the app | Top bar with the shield mark and **Kompas Krakowa**, a **Home page** link, an online indicator, a language selector. A map of Kraków with coloured 250 m squares, a mode switch and a menu panel | |
| RES-02 | H | Mode switch | Click **Night safety**, **Heat relief**, **Flood**, then **Air** | **Exactly these four** options exist, in this order. The map recolours; the score names, legend and available report types follow the mode | |
| RES-03 | H | Colours and legend | Open the colour key (layers button) in each mode | Four bands: Good (blue), Fair (light blue), Weak (light red), Critical (red), each with its score range and meaning. Heat ranges read 0–25 low heat up to 65–100 very high heat. Grey squares mean no mapped streets. Swatches have an outline and are readable | |
| RES-04 | H | Heat direction | Tap a hot, shadeless square and a leafy one in **Heat relief** | The card is titled **Heat-relief score** and says **Higher = more heat relief**. The hot square shows a LOW number, a SHORT bar and a RED band; the leafy square a HIGH number, a LONG bar and a BLUE band. The legend lists 75–100 Good (low heat) down to 0–35 Critical (very high heat) | |
| RES-04a | H | Same direction everywhere | Compare a score card in each of the four tabs | In every tab a high number, a long bar and blue mean good; low, short and red mean bad; the "!" explanation says so | |
| RES-05 | M | Conditions strip | Look under the mode switch | Live conditions (time of day or sunrise for night, temperature and heat risk, air quality). If the live conditions suggest the other mode, a **Switch to …** suggestion appears and works | |
| RES-06 | M | Map controls | Zoom with buttons, scroll and pinch; click **Use my location**; click **Places on the map**, zoom to 14 or closer | Zoom works. Location asks for permission and centres the map (a refusal shows a friendly message, not an error page). Place markers (fountains, parks, toilets, refuges, night-open places, defibrillators) appear from zoom 14 and match the mode | |
| RES-07 | M | Saved mode | Choose **Heat relief**, reload | The app reopens in **Heat relief** | |
| RES-08 | M | Stale saved value | In DevTools set `localStorage.mode` to `both`, reload | The app starts in a valid mode (Night safety or Heat), no error, no blank screen | |

### 6.2 Search and place card

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RES-10 | H | Address search | In the menu type "Rynek Główny"; pick a suggestion | Suggestions appear while typing with an **All / Addresses / Stops** filter. Picking one centres the map and opens the place card for that address | |
| RES-11 | M | Search filters | Search "Dworzec", switch the filter to **Stops** | Only stops remain, labelled **Stop** | |
| RES-12 | M | Nothing found | Search `zzzzqqq` | "Nothing found. Try another spelling." | |
| RES-13 | H | Place card | Tap any lit square | A card with the address, coordinates, the radius covered, the **score with a band**, an **!** button, **why** (each factor with distance and weight), **nearby help** with walking times, and nearby reports. The map shows the dashed 1.5 km circle and the outlined 250 m square | |
| RES-14 | H | Factor names | Open a Heat card and read the factor list | The water factor is named **Drinking fountains and taps** (never plain "Drinking water"); other factors: Parks and shade, Cool indoor place, Public toilets, Public transport | |
| RES-15 | M | Thin water data | Tap a place far from any fountain | A note explains that only N fountains and taps are mapped and one may be missing from the map | |
| RES-16 | M | Outside the area | Tap the map far outside Kraków | "This spot is outside the mapped area." No crash | |
| RES-17 | M | Directions link | On a place card click **Directions** | Opens an external map in a new tab | |

### 6.3 Score explanations

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RES-20 | H | Explain a score | Click **!** next to a score | A panel explains what 0 and 100 mean, the colour ranges, each factor's weight and **why** it has that weight, the data source and its limits | |
| RES-21 | M | Explain a factor | Click a factor row | A factor panel with what it measures, the distances for full and zero score, the source | |
| RES-22 | M | Weights and sources | Click **Weights and sources** | The full method opens; the water source reads "OpenStreetMap drinking fountains and taps (amenity=drinking_water)" | |
| RES-23 | L | Close panels | Press **Esc**, click **Close** | The panel closes and focus returns to where it was | |

### 6.4 Walk check

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RES-30 | H | Night walk | In **Night safety** click **Check a night walk**; enter a start and a destination from suggestions | The route follows real streets. A **fastest** route is shown and, when clearly better, a **safer** alternative (at least 3 points better on average, at most 30 % longer). Start (A) and end (B) are marked with addresses. Each route shows time, length and score; the weakest spot and advice are listed | |
| RES-31 | H | Cool walk | In **Heat relief** click **Check a cool walk** with another pair | Same flow with a **cooler** alternative; advice mentions fountains and taps, shade and cool places | |
| RES-32 | M | Inputs by map or area | Tap the map to set the start; use **My area** and **Selected place** | Each fills the field with an address | |
| RES-33 | M | Compare routes | Click the other route | The selected route is solid; the other is a dashed grey line; the figures switch | |
| RES-34 | M | No better route | Pick two close points on one street | Only the fastest route is shown, with a message that no clearly better route exists | |
| RES-35 | M | Routing unavailable | Block `routing.openstreetmap.de` in DevTools, retry | A friendly error, and the rest of the app keeps working | |
| RES-36 | M | Back to menu | Click **Back to menu** | Returns to the menu with the map intact | |

### 6.5 My area and alerts (resident side)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RES-40 | H | Set my area | Click **Set my area** (allow location, or choose a map point) | The menu says "Alerts are on for your area". The position is stored only on the device | |
| RES-41 | M | Notifications | Click **Notify me on this device** | The browser asks for permission; allowed shows "Notifications on"; blocked shows the blocked message; unsupported browsers show the unsupported message | |
| RES-42 | H | Receive a planner alert | See **E2E-30** | The alert shows as a banner while the app is open | |

---

### 6.6 Flood and air views

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RES-50 | H | Flood map and legend | Choose **Flood**; open the colour key | The map recolours; the legend says **Flood safety score** and **Higher = safer from flooding**, with the same four bands (75–100 Good … 0–35 Critical) | |
| RES-51 | H | Flood place card | In **Flood** tap a square near the Wisła, then one 1 km away from any river | The riverside square scores clearly lower. The card lists **Distance from rivers and streams** (with the river's name and metres), **Hospital or police nearby** and **Way out (public transport)**, with help and exits nearby and a note that the score has no ground-height or flood-map data | |
| RES-52 | H | Air map and place card | Choose **Air**; tap a square next to a main road and one in a park | The road-side square scores lower. Factors: **Distance from main roads**, **Parks and trees**, **Indoor place to wait out bad air**. The conditions strip shows the air band and PM2.5 | |
| RES-53 | M | Live river level | While IMGW shows a gauge above warning (or with test data) open **Flood** | The strip shows "Rivers above warning: N"; riverside squares are lower than with calm rivers; the menu suggests the Flood view | |
| RES-54 | M | Smog | While PM2.5 averages above 45 µg/m³ open **Air** | All squares are lower than on a clean day, the strip shows PM2.5, and the menu suggests the Air view | |
| RES-55 | M | Walk checks | In **Flood** and **Air** start a walk check between two points | A fastest route and, when clearly better, a **Safest from water** / **Cleanest air** alternative, with average and worst values and advice in the mode's words | |
| RES-58 | H | Path labels | Open the menu in **Flood** and **Air** | The buttons read **Check a flood-safe path** and **Check a clean-air path**; the place card offers **Flood-safe path here** / **Clean-air path here**; the path view is titled **Flood-safe path check** / **Clean-air path check** | |
| RES-59 | H | Weak fastest route widens the search | In **Night safety** pick two points about 3 km apart across a poorly lit area | The note says the fastest route is weak and a safer one was searched for farther away; the safer route may be much longer (up to about 2x, at most 3 km more) and scores clearly better; the extra minutes are stated | |
| RES-60 | H | Good fastest route does not widen | Pick two points on a well-lit, well-served street | No "searched farther" note; at most a nearby alternative with a small detour (max 30 % longer) | |
| RES-61 | M | Nothing better exists | Pick a pair where every street is poor | The app says no route within reach is clearly better and advises extra care or another way to travel | |
| RES-56 | M | Rivers not loaded | Start the API with no saved OpenStreetMap places and open **Flood** at once | The river factor reads "unknown (not loaded)" at 50, a note explains it, and the planner overview says datasets are still loading; after loading it shows real distances | |
| RES-57 | M | Mode memory and fallback | Choose **Air**, reload; then set `localStorage.mode` to `both` and reload | Air is remembered; a stale value falls back to a valid tab without errors | |

## 7. Resident report flow (`REP`)

Report types depend on the mode:

| Mode | Types offered |
| --- | --- |
| Night safety | Street light out or too dark, Feels unsafe at night, Blocked or hazardous path |
| Heat | Water point not working, No shade (very hot spot), Overheated area with no relief nearby |

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| REP-01 | H | Types follow the mode | In **Night safety** tap a square, click **Report a concern**; go back and repeat in **Heat relief** | Night safety offers exactly the three night types, Heat the three heat types. Each type has an icon and the layer name. A red note says to call emergency services for emergencies | |
| REP-01a | H | Flood and air report types | Repeat REP-01 in **Flood** and **Air** | Flood offers *Flooded street or underpass*, *Blocked drain or gully*, *River or stream rising fast*; Air offers *Smoke or burning smell*, *Strong fumes or chemical smell*, *Dust cloud or construction dust*; each with its own icon and layer name | |
| REP-02 | H | Choose a type | Click one type | The card is pressed; **Next** becomes enabled. Without a choice **Next** is disabled | |
| REP-03 | H | Place the pin | On step 2 drag the pin; tap the map; type an address and pick a result; use **Use selected place** or **Use my area** | The address, coordinates and outlined 250 m square update each time and say what the report counts for | |
| REP-04 | H | Add a note | Type a note | A counter shows `n/200`; typing past 200 characters is blocked; a privacy note says the note is only visible to planners | |
| REP-05 | H | Send a report | Click **Send report** | A "Sending…" state, then **Thank you!** with "Your report was added. When another person confirms it, it counts in full." | |
| REP-06 | H | Score changes | Click **See the updated score** | The place card shows a note that recent reports lower the score here, and the report in **Reports near here**. For a night report the night-safety score is lower; for a heat report the heat-relief score is **lower**. A single unconfirmed report moves it only slightly | |
| REP-07 | H | Second resident confirms by reporting | As **resident B** (other browser profile), report the **same type** in the **same 250 m square** within 24 h | B sees "2 people have now reported this here, so it counts in full." No duplicate report is created; the score effect grows | |
| REP-08 | H | Confirm with "Still true" | As resident B open the first report on a place card and click **Still true** | The button changes to **Confirmed** with "Thanks for confirming". Supporters rise to 2 | |
| REP-09 | M | Same device cannot self-confirm | As resident A click **Still true** on A's own report | The count of supporters does not increase | |
| REP-10 | M | Different square or type | A reports a different type or a square 500 m away | A **new** report is created, not merged | |
| REP-11 | M | Rate limit | From one browser send 6 reports in an hour (different squares or types) | The 6th shows "Too many requests. Please wait a bit." (HTTP 429); the first 5 are saved | |
| REP-12 | M | Outside Kraków | Place the pin far outside Kraków and send | A clear validation message; nothing is saved | |
| REP-13 | M | Offline queue | Go offline (DevTools Network, Offline), send a report | The button reads **Save to send later**; the result is **Saved on your device**. Go online: the report is sent automatically and appears on the planner (see OFF-02) | |
| REP-14 | M | Back and change | On step 2 click **Change** and **Back** | Returns to step 1 keeping the chosen type; the note is kept | |
| REP-15 | M | Report another | After sending click **Report something else** | A fresh form starts at the same location | |
| REP-16 | M | Note safety | Send a note containing `<script>alert(1)</script>` and `<b>x</b>` | The planner shows it as plain text; no script runs; markup is not rendered | |
| REP-17 | L | Resident view of notes | As a resident open the report on a place card | The note is **not** visible to residents, only to planners | |

## 8. Planner dashboard (`PLN`)

Open `#/planner`. If a sign-in screen shows (demo key emptied), enter `demo-planner`.

### 8.1 Access and shell

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-01 | H | Demo access | Open the planner link from the home tile | The dashboard opens directly; a **Demo access** chip is visible | |
| PLN-02 | H | Sign-in | Empty `Safety:PlannerDemoKey`, restart, open the planner | A sign-in form. A wrong key shows an error; `demo-planner` signs in; **Sign out** returns to the form | |
| PLN-03 | H | Event selector | Switch **Heat relief**, **Night safety**, **Flood** and **Air quality** | **Exactly these four** options exist. Ranking, charts, map, report lists and drawers all change to the chosen event; the other event's content is hidden | |
| PLN-04 | M | Navigation | Click Overview, Map, Reports, Alerts, Contacts | Each page loads; the active item is marked; every page except Overview has **Back to dashboard** | |
| PLN-05 | M | Conditions and freshness | Look at the tools bar | Live conditions chip, "Updated … ago" text, and a refresh button that reloads the data | |
| PLN-06 | M | Switch to resident | Click **Resident view**, then back | Resident app opens; the planner can be reopened from its link | |
| PLN-07 | M | Saved event | Choose **Night safety**, reload | The planner reopens on **Night safety** | |
| PLN-08 | M | Stale saved value | Set `localStorage.plannerEvent` to `both`, reload | Opens on **Heat relief**, no error | |

### 8.2 Overview

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-10 | H | Headline numbers | Open Overview in each event | A situation banner, KPI tiles (average score, critical squares, missing water or lighting, open reports, active alerts, devices active), a score distribution, "what is missing most", and **where to act first** (ranked list) | |
| PLN-11 | H | Every figure explains itself | Click a KPI, a bar, a ranked row | A panel shows how it is computed and the data source; the missing-water KPI reads **No drinking fountain nearby** | |
| PLN-12 | M | Filter by gap | Click a bar in "what is missing most" | The ranked list filters to that gap; clicking again clears it | |
| PLN-13 | M | Open an area | Click a row in the ranked list | The area drawer opens (see 8.4) | |
| PLN-14 | M | Table view | Toggle the table view of a chart | The same numbers appear as a table | |
| PLN-15 | M | Data-gap note | Read the notes at the bottom | A note says how many drinking fountains and taps are mapped and that low water scores may be partly missing map data | |

### 8.3 Map

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-20 | H | Default colours | Open **Map** | It opens on **Score**, using the **same four-band colours and legend** as the resident map (blue good to red critical, with ranges). It does **not** open on a red-only ramp | |
| PLN-21 | M | Priority option | Switch to **Priority** and back | A red ramp with lower and higher labels shows for Priority; the score colours return for Score | |
| PLN-22 | H | Legend contrast | Read the legend over the map | Swatches are outlined, the text is readable, and the ranges match the resident legend | |
| PLN-23 | H | Reports and alerts on the map | Tick and untick **Resident reports** and **Alert areas** | Report markers (by type) and alert circles appear and disappear. Heat shows heat reports only; Night safety shows night reports only | |
| PLN-24 | M | Drill down | Tap a square | The square is highlighted, its address is shown, and the area drawer opens | |
| PLN-25 | M | Places layer | Tick **Places on the map**, zoom in | Places relevant to the event appear | |
| PLN-26 | M | New alert from the map | Click **New alert on map**, tap the map | The alert composer opens with that point as the centre | |

### 8.4 Area drawer

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-30 | H | Drawer content | Open a drawer | Scores for the event only, factor table (factor, weight, value, score, contribution), nearest assets, resident reports in that square (with notes), and **suggested actions** (e.g. install a drinking fountain or tap, add shade, repair lighting) | |
| PLN-31 | M | Actions | Use **Create alert** and **Contact an agency** from the drawer | The alert composer and the contact brief open, prefilled with this area | |
| PLN-32 | M | Close | Press **Esc** or **Close** | The drawer closes; focus returns to the trigger | |

### 8.5 Reports page

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-40 | H | List and counts | Open **Reports** | A list of resident reports with type, area, time, supporters, note, and a verification status. A count line ("N reports") | |
| PLN-40a | H | Flood and air dashboards | Choose **Flood**, then **Air quality** | Flood: KPIs *Within 200 m of a river*, *No hospital or police within 1 km*, *River situation*. Air: *Within 100 m of a main road*, *No park within 500 m*, *Air pollution now*. The conditions chip shows the rivers or PM2.5, the legend says Flood safety score or Clean-air score, and the area drawer lists that layer's factors, report types and suggested actions only | |
| PLN-41 | H | Filters | Use **Status** (Open, Resolved, All), **Type**, **Verification** and **Search** | The list and the count update; a "no reports match" message appears when empty. Each filter has a visible label | |
| PLN-42 | H | Event filter | Switch the event | Heat shows heat types only; Night safety shows night types only; the note above the list says which | |
| PLN-43 | H | Verify | Click **Verify** on an unconfirmed report | A "Report verified" toast; the report shows verified; the score effect grows to full strength | |
| PLN-44 | H | Resolve | Click **Resolve**, add "Lamp replaced on 3 Oct", confirm | A "Report resolved. The score recovers." toast; the report leaves **Open** and shows under **Resolved** with the resolution text; the area score recovers | |
| PLN-45 | M | Show on map and open area | Click **Show on map**, then **Open area** | The map centres on the report at street zoom; the drawer opens for that square | |
| PLN-46 | M | Alert from a report | Click **Alert** on an open report | The composer opens centred on the report | |
| PLN-47 | M | Add demo reports | Click **Add demo reports** twice | First click: "Added N demo reports in the lowest-scoring areas". Second click: "Demo reports already exist" | |
| PLN-48 | H | Filters are applied by the API | Reports: set **Status**, **Type**, **Verification** and search text one at a time and together; switch the event | Each change re-fetches and the count matches; filters are not cut short by the 500-report limit; search also finds the type name in the language on screen and the planner's notes; a resident cannot find notes by searching | |
| RES-18 | M | Search filter chips | Resident menu: search "Dworzec", then click **Stops** and **Addresses** | The list narrows to that kind; when the kind has no result the message "Nothing found" shows instead of a silent empty list | |

### 8.6 Alerts page (planner side)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-50 | H | Compose and send | **Alerts**, **New alert**; start from a template; set centre, radius, severity, duration; read the reach estimate and preview; click **Send alert**, then **Press again to confirm** | A two-step send. Toast "Alert sent. About N phone(s) in the area now." The alert appears under **Active** | |
| PLN-51 | M | Validation | Try to send without a title, message or area | The send is blocked with clear field messages | |
| PLN-52 | M | Translations | Add a Polish and a Ukrainian text | Residents see the text in their language, otherwise the main message | |
| PLN-53 | H | Cancel | Click **Cancel alert** on an active alert and confirm | "Alert cancelled." It moves to **Earlier alerts** as cancelled; residents stop seeing it on their next check (about a minute) | |
| PLN-54 | M | Expiry | Send a short alert | After its duration it moves to **Earlier alerts** and disappears from residents | |

### 8.7 Contacts page

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PLN-60 | M | Agency list | Open **Contacts** | Agencies with description, phone ("verify number" tag), website and **New contact**: ZDMK, city services portal, Crisis Management Centre, Straż Miejska, ZZM, ZTP. A banner says nothing is sent | |
| PLN-61 | M | Brief and log | Click **New contact**; choose Polish or English; save | A prefilled brief states location, scores, weak factors, open reports and the requested action. A log entry appears with time, agency, subject, reference and a **simulated** delivery tag | |
| PLN-62 | L | Copy | Click **Copy text** in the brief dialog | The brief is on the clipboard | |
| PLN-63 | H | Staff-only marking | Open the planner dashboard | The top bar has an amber underline and a **City staff only** chip; the same marking is used in the Blazor sidebar and on the home tile | |
| PLN-70 | H | Weights page | Planner, **Weights** | Four layers (night safety, heat, flood, air), the one for the current event open; every factor shows its weight, the default, a slider and a number box, and **why it has that weight**, with what it measures, source and limits under "more" | |
| PLN-71 | H | Change and save weights | Set Heat > Drinking fountains and taps to 75, **Save weights** | The other factors shrink proportionally (each layer shows its scaled shares adding up to 100); a "custom" chip appears; a toast confirms; the resident app and the planner now score with the new weights (the method panel shows 50 vs default 25) | |
| PLN-72 | M | Reset | **Reset all to defaults** and confirm; or **Use the defaults for this score** then Save | Weights return to the defaults, "custom" disappears, scores return | |
| PLN-73 | M | Validation | Set every factor of a layer to 0 and save; type a negative number | The save is refused with a clear message; negative numbers are not accepted | |
| PLN-74 | M | Persistence | Save weights, restart the API | The custom weights are still in use | |
| PLN-75 | M | Weights page on a phone | Open at 375 px | Sliders and number boxes fit, reasons wrap, no sideways scrolling, six bottom-navigation items fit | |

---

## 9. End-to-end: a resident report appears on the planner dashboard (`E2E`)

This is the key integration scenario. Use **two windows**: **Resident A** (normal window, `/safety/index.html`) and **Planner** (private window, `/safety/index.html#/planner`). Optionally a third profile **Resident B**.

**Preparation:** with the API stopped, delete `safety-store.json` for a clean start (or note the existing counts). Start the API and Web app. In the planner open **Reports** and write down the **open report count** and, in **Overview**, the **Open reports** KPI. In the resident app pick one square and note its **night-safety score**.

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| E2E-01 | H | Create a night report | **Resident A**, mode **Night safety**: tap a square near a main street, **Report a concern**, choose **Street light out or too dark**, set the pin, note "Lamp out at the corner, very dark", **Send report** | "Thank you!" with the single-report message | |
| E2E-02 | H | Report is visible to the planner | **Planner**, event **Night safety**, **Reports** (refresh with the refresh button or switch page and back) | The new report is in the **Open** list: type **Street light out or too dark**, the area id of the square, "just now", **1 supporter**, **not verified**, and the full note "Lamp out at the corner, very dark". The count is one higher than the baseline | |
| E2E-03 | H | KPI and ranking update | Planner **Overview** | **Open reports** is one higher; the square's priority rises or its open-report count shows 1 in the ranked list | |
| E2E-04 | H | Map marker | Planner **Map**, tick **Resident reports** | A lamp marker at the pin location; its tooltip shows the type and supporters; clicking it opens the drawer for the square | |
| E2E-05 | H | Drawer shows it | In the drawer | The report appears under resident reports with its note and **Verify** and **Resolve** buttons; the safety score is lower than the baseline by a small amount (an unconfirmed report counts a quarter, about 3.8 points for a light-out report) | |
| E2E-06 | H | Event filtering | Planner switches to **Heat relief** | The night report is **not** listed on Reports, the map, or the drawer; switch back to **Night safety** and it returns | |
| E2E-07 | H | Resident score reflects it | **Resident A** reopens the same square | The night-safety score is lower than the baseline and the card says recent resident reports lower the score here | |
| E2E-08 | H | Confirmation increases weight | **Resident B** reports the same type in the same square (REP-07) or clicks **Still true** (REP-08) | B sees the confirmation message. The planner list now shows **2 supporters**; the safety score drops by about 19 points in total for a light-out report (from about 3.8 to about 18.8), up to the cap | |
| E2E-09 | H | Planner verifies | **Planner** clicks **Verify** | The toast appears, the report is marked verified, the effect counts in full even with 1 supporter | |
| E2E-10 | H | Planner resolves | **Planner** clicks **Resolve** with "Lamp replaced" | The toast says the score recovers; the report moves to **Resolved** with the resolution text; the open count falls by one | |
| E2E-11 | H | Resident sees recovery | **Resident A** refreshes the square | The report no longer lowers the score; the score is back near the baseline | |
| E2E-12 | H | Heat report round trip | Repeat E2E-01 to E2E-05 with **Heat relief** mode and **Overheated area, no relief nearby**, planner on **Heat relief** | Same flow. The report shows in the planner's heat view only; the **heat-relief** score of the square is **lower** (less relief) | |
| E2E-12a | H | Flood report round trip | Repeat E2E-01 to E2E-10 with **Flood** and *Flooded street or underpass* near a river; planner on **Flood** | Same flow. The report is listed under Flood only (not under Heat, Night safety or Air), the map marker has the flood icon, the flood-safety score of the square drops, verify and resolve work, and the score recovers | |
| E2E-12b | H | Air report round trip | Repeat with **Air** and *Strong fumes or chemical smell*; planner on **Air quality** | Same flow for the clean-air score | |
| E2E-12c | M | Flood alert | Planner **Alerts**, template **Flood: river rising**; resident has *my area* set in the **Flood** view | The resident sees the alert only in the Flood view (and as a general alert if sent as General); the layer shows **Flood** | |
| E2E-13 | M | Offline report arrives | **Resident A** goes offline, sends a report (REP-13), goes online | Within about a minute (or on reconnect) the report appears on the planner Reports list with the correct time of sending | |
| E2E-14 | M | Planner alert reaches the resident | **Resident A** sets **my area** near the report (RES-40). The **Planner** opens the drawer of that square, **Create alert**, template for the event, send and confirm | Within about a minute **Resident A** sees the alert banner (title and message in A's language) in the matching mode. After the planner cancels it (PLN-53) the banner disappears on the next check | |
| E2E-15 | M | Report from another area is not shown in the drawer | Open a distant square's drawer | The report from E2E-01 is **not** listed there (reports affect only their own square) | |
| E2E-16 | M | Rate-limited resident | After 5 reports in an hour from A | The 6th is rejected (REP-11) and **no** extra report appears on the planner | |

---

## 10. API (`API`)

Use Swagger at http://localhost:5080/swagger or any HTTP client.

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| API-01 | H | Swagger | Open `/swagger` | Title **Kompas Krakowa API**; all groups (catalog, mobility, environment, crisis, urban, services, open data, safety) are listed | |
| API-02 | H | Catalog | `GET /api/catalog/categories` | 200 with categories and datasets (key, title, notes, access, publisher, apiRoute) | |
| API-03 | M | Dataset endpoints | Call one endpoint per category (stops, weather, air quality, rivers, warnings, districts, amenities, street lights) | 200 with data, or a clear 503 with a message while a source is still loading | |
| API-04 | H | Safety score | `GET /api/safety/place?lat=50.0614&lon=19.9372` (check the parameter names in Swagger) | Heat, safety and combined scores with bands, factors and nearest places; the water factor is labelled **Drinking fountains and taps** | |
| API-05 | M | Point outside Kraków | Request a score for a point far away | A 400 or "outside area" response, never a 500 | |
| API-06 | H | Create a report | `POST /api/safety/reports` with a report with a valid type, coordinates in Kraków and a device id | 200/201 with the report (id, type, supporters, status Open). A bad type, bad device id or location outside Kraków returns **400** with a problem description | |
| API-07 | M | Merge and confirm | `POST /api/safety/reports` with the same type in the same cell twice with different device ids; `POST /api/safety/reports/{id}/confirm` | The second call returns the same report with 2 supporters; confirmations do not double-count one device | |
| API-08 | H | Planner endpoints need the key | Call `/api/safety/planner/summary`, `.../reports/{id}/verify`, `.../reports/{id}/resolve`, `.../alerts`, `.../demo-data` with no `X-Planner-Key` header and with a wrong one | **401** without or with a wrong key; **200** with `X-Planner-Key: demo-planner` | |
| API-09 | M | Planner verify and resolve | `POST` verify and resolve on a report | Status and `verifiedByPlanner` change; a resolved report no longer affects the score | |
| API-10 | M | Alerts for a point | `POST /api/safety/planner/alerts`, then `GET /api/safety/alerts?lat=..&lon=..` for a point inside and a point outside the circle | The inside point returns it; the outside point returns none | |
| API-11 | M | Rate limit | Post 6 reports from one device in an hour | 429 "Too many reports" on the 6th | |
| API-12 | M | CORS | Call the API from `http://localhost:5090` and from another origin | Allowed from the configured origin only | |
| API-13 | M | Method and grid | `GET` the method and the grid | Documents of the factors, weights and sources; the grid lists squares with thresholds | |
| API-14 | L | Planner demo data | `POST` demo data twice | First creates demo reports, second reports that they already exist | |

---

## 11. Usability, responsiveness, accessibility, languages, offline

### 11.1 Usability (`UX`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| UX-01 | H | First-time resident task | Give a new person no help: "Find out how safe the street where you live is at night" | They find the place, read the score and understand the colour within **2 minutes**; they find **Night safety** without help | |
| UX-02 | H | Report task | "Report a broken street light" | They complete the report in **under 2 minutes** and understand what happens next | |
| UX-03 | M | Planner task | "Find the area that most needs action during a heatwave and the reports about it" | They switch to **Heat relief**, use the ranked list and the drawer without help | |
| UX-04 | M | Consistent naming | Read the home page, menus, titles and tooltips | The product is always **Kompas Krakowa**; the catalog is always **Available Open Data**; the water factor always says fountains and taps; no leftover "Kraków Open Data" title, "Raw data catalog" or "Both" option (data-source names like "City of Kraków Open Data API" are allowed) | |
| UX-05 | M | Navigation | From every page find the way back | Home is always reachable; the resident and planner views have **Home page**; planner sub-pages have **Back to dashboard** | |
| UX-06 | M | Feedback | Trigger loading, success, error and empty states in several places | Each shows a clear message in the current language with a next step (Retry, Back) | |
| UX-07 | M | Branding | Review colours and icons across the site | Ink-dark top bar with a cyan line, the shield mark everywhere (favicon, top bar, home hero), cyan and green accents; consistent between the Blazor pages and the `/safety/` app | |
| UX-08 | L | Install | In Chrome use **Install app** on `/safety/` | Installs with the shield icon and the name **Kompas Krakowa**; opens standalone with the dark theme colour | |

### 11.2 Responsive layout (`RSP`)

Test at **320, 360, 375, 390, 412, 768** px, portrait and landscape. **Pass rule at every width: the page never scrolls horizontally** (no horizontal scrollbar on the page, nothing cut off at the right edge, no inner box you must scroll sideways).

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| RSP-01 | H | Home | Open `/` at each width | Hero and tiles stack in one column; text wraps; the mark stays behind the title and the text remains readable | |
| RSP-02 | H | Top bars | Look at the Blazor top bar and the `/safety/` top bar | Name, menu button, language and status fit; below 375 px the Blazor bar may hide the wordmark but keeps the logo; the `/safety/` bar wraps to a second row instead of clipping | |
| RSP-03 | H | Sidebar | At phone width tap the menu (☰) in the Blazor app | The sidebar slides in over the page; tapping the button again closes it; links work | |
| RSP-04 | H | Tables become cards | Open Stops, Vehicles, Districts, Amenities, NFZ waiting lists and an Open Data table at 375 px | Each row becomes a card with **column title and value pairs**; there is **no sideways scroll** | |
| RSP-05 | H | Planner tables | Open the planner Contacts log, a drawer factor table and the "table view" of a chart at 375 px | They show as stacked cards, no sideways scroll | |
| RSP-06 | H | Resident bottom sheet | Use the resident app at 375 px; open a place card with long place names and a **Cool indoor place** factor | The card fits; long names wrap or truncate with an ellipsis; no sideways scroll | |
| RSP-07 | M | Forms | Open Stops search and the report form at 320 px | Inputs and buttons are full width, labels do not overlap, touch targets are at least 44 × 44 px | |
| RSP-08 | M | Planner at phone width | Open Overview, Map, Reports, Alerts, Contacts | The bottom navigation shows five items; content fits; the map and the drawer are usable | |
| RSP-09 | M | Landscape | Rotate a phone | Layouts adapt; nothing is hidden behind the notch or the bottom bar | |
| RSP-10 | M | Wide screens | Open at 1280, 1440 and 1920 px | The sidebar is visible, content uses the width sensibly, the resident app shows a side panel | |
| RSP-11 | M | Zoom | Browser zoom to 200 % and text size to large | No loss of content or function; no sideways scroll at 200 % on a 1280 px window | |

### 11.3 Accessibility (`A11Y`)

Target: WCAG 2.1 AA.

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| A11Y-01 | H | Text contrast | Check text against its background with a contrast tool or the browser's accessibility panel on every page type, in light and dark (`/safety/`) | Normal text at least **4.5:1**, large text at least **3:1**, including muted text, outline buttons, code, sidebar headings, the bus-line chip, the **Good** band chip and badges. Disabled controls are exempt | |
| A11Y-02 | H | Non-text contrast | Check borders of inputs, focus rings, map legend swatches, icons | At least **3:1** against the background | |
| A11Y-03 | H | Colour is not the only signal | Read the maps, chips and status badges | Every band or status also has a **text label** (Good, Fair, Weak, Critical; Open, Resolved) | |
| A11Y-04 | H | Keyboard only | Use Tab, Shift+Tab, Enter, Space and Esc through the home page, catalog, resident app (menu, search, report flow) and planner | Every control is reachable and operable; the order is logical; there is no keyboard trap; **Esc** closes dialogs and panels | |
| A11Y-05 | H | Focus visible | Tab through the pages | A clear focus ring (cyan/high contrast) is always visible, also on the dark bars | |
| A11Y-06 | M | Skip link | On the safety app press Tab once | A **Skip to content** link appears and jumps to the main content | |
| A11Y-07 | M | Screen reader | Use NVDA, VoiceOver or TalkBack on the home page, a place card and the report form | Landmarks, headings, button names and form labels are announced; the mode switch announces its pressed state; toasts and results are announced (live regions); the logo image has no noisy text | |
| A11Y-08 | M | Labels | Inspect every input and icon-only button | Each has a visible label or an accessible name (for example the menu button, the layers button, the language select) | |
| A11Y-09 | M | Page language | Switch language | The `lang` attribute of the page follows the choice | |
| A11Y-10 | M | Motion and zoom | Check for animations; pinch-zoom on a phone | Zoom is not disabled; animations are short and non-essential | |
| A11Y-11 | L | Automated scan | Run Lighthouse accessibility or axe on `/`, `/catalog`, `/safety/index.html` and the planner | No critical or serious violations; Lighthouse score at least 95 | |

### 11.4 Languages (`I18N`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| I18N-01 | H | Blazor switcher | Click **PL**, **EN**, **UA** in the top bar | All page chrome, menu items, tile text, table headings and messages change; the choice persists after a reload; a first visit follows the browser language | |
| I18N-02 | H | Resident language | Choose Polish, then Ukrainian in the resident language select | All text changes including the map legend, report types, scores and messages; no raw keys such as `report.send` appear | |
| I18N-03 | H | Planner language | Same in the planner | All pages, KPIs, drawers and alert templates translate | |
| I18N-04 | M | Layout in Polish and Ukrainian | Review long strings at 320 px | No clipping or overlap | |
| I18N-05 | M | City data | View stop names, alert texts and table columns | They are shown **as published** (not translated); this is expected | |
| I18N-06 | M | Names | Check the brand in all three languages | **Kompas Krakowa** in all three; **Available Open Data** appears as *Dostępne otwarte dane* and *Доступні відкриті дані* | |

### 11.5 Offline and installability (`OFF`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| OFF-01 | H | Offline use | Load the resident app, then go offline and reload | The app shell loads; saved scores show with a **saved** label; an offline banner is visible; search says it needs a connection but tapping the map still works | |
| OFF-02 | H | Offline report | REP-13 | The report is queued and sent after reconnect, then visible on the planner (E2E-13) | |
| OFF-03 | M | Planner offline | Load the planner, go offline | Saved data shows with a stale banner; actions that need a connection (verify, resolve, send alert) explain that they need one | |
| OFF-04 | M | Update | Deploy a new version and reopen | The service worker picks up the new files on the next load; no stale layout | |
| OFF-05 | M | API unavailable | Stop the API with the app open | A friendly error, saved data still shows, no blank page; recovery after the API is back | |

---

## 12. Security and robustness (`SEC`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| SEC-01 | H | Planner protected | Call planner endpoints without a key (API-08) | 401 | |
| SEC-02 | H | Demo key warning | Review configuration | `Safety:PlannerKey` and `Safety:PlannerDemoKey` are the demo values and **must be changed or emptied outside demos** (the README says so) | |
| SEC-03 | H | Injection in notes and alert text | Put markup and `javascript:` text in notes, alert titles and messages | Shown as plain text everywhere; no script executes | |
| SEC-04 | M | Input validation | Send oversized notes, bad ids, negative or huge coordinates, wrong types | 400 with a clear message; never a 500 or a stack trace | |
| SEC-05 | M | Privacy | Inspect network calls and storage | Only an anonymous random device id and the user's chosen area are stored on the device; positions are not stored on the server; notes are visible only to planners | |
| SEC-06 | M | HTTPS and headers | Review the deployed site | Served over HTTPS; no mixed content warnings in the console | |
| SEC-07 | L | Console | Browse all pages with DevTools open | No uncaught errors; no failed requests except those caused by tests | |

## 13. Performance (`PERF`)

| Id | Pri | Scenario | Steps | Expected result | Result |
| --- | --- | --- | --- | --- | --- |
| PERF-01 | M | Resident first load | Warm API, fast 4G profile | The map and scores are usable within about **5 s** | |
| PERF-02 | M | Planner load | Open the planner overview | Skeleton loaders show at once; content appears within about **6 s** | |
| PERF-03 | M | Interactions | Tap squares, switch modes and events | Responses appear within about **1 s** after the data is loaded | |
| PERF-04 | L | Large tables | Open an Open Data table with many rows | The page stays responsive; paging or limits apply | |

---

## 14. Automated checks (run first)

| Command | Expect |
| --- | --- |
| `dotnet test KrakowOpenData.sln` | All green: Domain 69, Application 145, Api 64, Web 787, Infrastructure 84 (total 1,149 at the time of writing) |
| Web tests | Every UI string has English, Polish and Ukrainian; links into `/safety/` carry `target="_top"`; every dataset page and component renders |

## 15. Traceability: features to cases

| Feature | Cases |
| --- | --- |
| Home with three tiles and navigation | HOME-01 to HOME-10 |
| Available Open Data catalog and dataset pages | CAT-01 to CAT-25 |
| Resident map, modes, legend, search, place card | RES-01 to RES-17 |
| Score explanations and water-factor naming | RES-14, RES-20 to RES-22, PLN-11, PLN-15 |
| Walk check | RES-30 to RES-36 |
| My area, alerts and notifications | RES-40 to RES-42, PLN-50 to PLN-54, E2E-14 |
| **Report creation by a resident** | REP-01 to REP-17 |
| **Report appears on the planner dashboard** | E2E-01 to E2E-16, PLN-40 to PLN-47 |
| Planner overview, map, drawer | PLN-10 to PLN-32 |
| Planner contacts | PLN-60 to PLN-62 |
| API | API-01 to API-14 |
| Responsive, accessible, multilingual, offline | RSP, A11Y, I18N, OFF |
| Security and performance | SEC, PERF |

## 16. Defect report template

```
Id:            DEF-___
Case id:       e.g. E2E-02
Environment:   OS, browser and version, device, viewport, language, light or dark
Build:         git commit
Steps:         1) 2) 3)
Expected:
Actual:
Evidence:      screenshot or video, console errors, network response
Severity:      critical | high | medium | low
```
