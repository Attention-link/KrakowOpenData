# Accessibility data (Dostępność / Kraków bez barier)

How the "Dostępność" feature of Kompas Krakowa gets, normalises and labels its data. Concepts and wording are ported from
the sibling project *Kraków bez barier* (`docs/DATA-FORMAT.md`, `data/normalise.py`, `web/src/prefs/model.ts`).

## Source and licence

| Data | Source | Licence | Refreshed |
| --- | --- | --- | --- |
| Steps, kerbs, lifts, entrances, places with `wheelchair` tags, toilets, benches, tactile paving, path surface / smoothness / slope / width | OpenStreetMap via Overpass (`out center tags meta` + `out geom tags meta`) | ODbL 1.0, © OpenStreetMap contributors | daily, background download, file cache (`osm-access-v1.json`) |
| Wheelchair flag of public transport stops | ZTP Kraków GTFS `stops.txt` (`wheelchair_boarding`) | ZTP open data terms | 6 h |
| Resident reports about a barrier | this app (`PathHazard` reports, anonymous device id) | — | live |

Area: `KrakowData:AccessArea` (default central Kraków, 19.90–19.99 E, 50.035–50.08 N). The full city with path geometry is a
very large Overpass answer; widening the box is a configuration change. Outside the box every point is "no data" and the API
says so (`dataNoteCode = outside_area`).

Query and parser: `src/KrakowOpenData.Infrastructure/OpenStreetMap/AccessFeaturesParser.cs`, normalisation:
`AccessTagNormaliser.cs`, rules per profile: `src/KrakowOpenData.Application/Accessibility/AccessProfile.cs`.

## Item format (`AccessItemDto`)

```json
{ "id": "way/102", "kind": "steps", "latitude": 50.063, "longitude": 19.9373, "name": null,
  "status": "no", "barrier": true,
  "facts": [ { "key": "step_count", "label": "Liczba stopni", "value": "5", "text": "5" },
             { "key": "ramp", "label": "Rampa", "value": "unknown", "text": "brak danych" },
             { "key": "handrail", "label": "Poręcz", "value": "yes", "text": "tak" } ],
  "source": "OpenStreetMap", "lastEdited": "2025-02-01T10:00:00+00:00", "reliability": "osm_recent",
  "osmUrl": "https://www.openstreetmap.org/way/102", "editUrl": "https://www.openstreetmap.org/edit?way=102",
  "distanceMeters": 145 }
```

- `kind`: steps, kerb, elevator, entrance, place, toilets, bench, tactile, path.
- `status`: yes | limited | no | unknown, **for the chosen profile**. `unknown` is shown grey as "brak danych" and is never accessible.
- `facts`: the facts that matter for the kind are always listed, with `value: "unknown"` / "brak danych" when OSM says nothing,
  so an item is never a bare accessible / inaccessible.
- Missing tags stay `null` in the domain model; nothing gets a default value.

## Normalisation (never guess)

| Tag | Normalised value |
| --- | --- |
| `surface` (+ `smoothness`) | `paved_smooth` (asphalt, concrete, paving stones with good smoothness …), `paved_rough` (sett, cobblestone, or any paved surface with smoothness intermediate or worse), `unpaved` (gravel, dirt, grass …). An unknown surface value gives null. |
| `incline` | absolute percent; `2°` is converted; `up`/`down`/`yes` (no number) give null |
| `width`, `door:width` | metres; `90 cm`, `0,9` accepted; feet or ranges give null |
| `kerb` | `raised`, `lowered` (`rolled` too), `flush` (`kerb=no` too); `kerb=yes` gives null (height unknown) |
| `ramp:wheelchair`, `ramp:stroller`, `ramp` | `wheelchair` / `stroller` / `yes`; all given as `no` → "no ramp" |
| `wheelchair`, `toilets:wheelchair` | `yes` (`designated` too), `limited`, `no` |

## Reliability levels

| Code | Meaning |
| --- | --- |
| `confirmed` | Owner data or three independent confirmations. None yet. |
| `osm_recent` | OSM element last edited within 24 months. |
| `osm_old` | OSM element last edited earlier. |
| `user_unverified` | A resident report. Always shown separately from mapped data. |
| `unknown` | No data. |

The date shown is the **last edit of the OSM element** ("ostatnia edycja w OpenStreetMap"), not a check on site; the app never
says "verified".

## Profiles (preferences, not diagnoses)

| | Wheelchair | Pram | Limited mobility |
| --- | --- | --- | --- |
| Steps without a suitable ramp | no (barrier) | no (barrier) | limited |
| Ramp tagged for prams only | limited | yes | yes |
| Raised kerb | no | no | limited |
| Rough surface (sett, cobbles) | limited | limited | limited |
| Unpaved | no | limited | limited |
| Slope limit | 6 % | 8 % | 8 % (limited) |
| Narrower than | 0.9 m | 0.8 m | — |

No health or disability information is asked or stored; the chosen profile stays in the browser (`localStorage`, wrapped in try/catch).

## Route check

`GET /api/safety/access/route?from&to&profile` (fastest street route) or `POST /api/safety/access/route` with a path. The path is
sampled every 10 m; for each sample the closest mapped path within 15 m gives its surface and slope (no match, or a path without
a surface tag, counts as "no data"); points (kerbs, lifts, toilets, benches) within 15 m are counted; steps and paths count only
where the route follows them (closest line, or within 5 m). The summary lists steps without a ramp, raised kerbs, metres of rough
or unpaved surface, metres steeper than the profile's limit, and the share of the route without surface data. The router itself
does not avoid barriers yet: it is a check, not a barrier-free route.

## Corrections

1. **Popraw w OpenStreetMap**: every item links to `openstreetmap.org/edit?<type>=<id>` so anyone with an OSM account fixes the
   tag at the source; the next daily download picks it up.
2. **Zgłoś problem**: a closed choice (wrong / outdated / temporary obstacle / missing) plus an optional short note, sent as an
   anonymous `PathHazard` report. It is shown to residents as `user_unverified`, separately from mapped data, and to planners in
   the dashboard (verify / resolve). It never overwrites OSM data and never turns "no data" into "accessible".

## Sample data

No sample data is used for accessibility: the tests use a hand-written Overpass-shaped fixture (`tests/Fixtures/osm-access-sample.json`).
If items ever come from a source whose name contains "sample", the API sets `sampleData: true` and the app shows "Dane przykładowe".

## Accessibility as a score layer (fifth layer)

The same data also feeds a 0–100 score per 250 m square (higher = easier, the usual bands Good ≥ 75, Fair ≥ 55, Weak ≥ 35, Critical), **one score per profile**
(`wheelchair` = layer `Access`, the default; `pram` = `AccessPram`; `mobility` = `AccessMobility`). Code: `Application/Safety/AccessLayer.cs` (factors, weights, counts),
`AccessMethodText.cs` (the written reasons), `StaticSafetyModel.Measure`, `ScoreService`.

| Factor (`access.{profile}.<factor>`) | Wheelchair | Pram | Mobility | What it measures (profile rules from `AccessRules`) |
| --- | --- | --- | --- | --- |
| `steps` | 22 | 15 | 10 | weighted steps within 150 m with no suitable ramp (steps and stepped entrances; limited counts 0.5) |
| `kerbs` | 20 | 15 | 5 | weighted raised kerbs within 150 m |
| `surface` | 14 | 20 | 12 | share of mapped footways within 150 m that are smooth, within the slope limit and wide enough |
| `slope` | 8 | 10 | 18 | footways within 150 m steeper than the profile limit (6 % / 8 % / 8 %) |
| `stepFree` | 10 | 8 | 8 | distance to the nearest step-free entrance, lift or place with `wheelchair=yes` |
| `accessStops` | 10 | 12 | 12 | distance to the nearest ZTP stop with `wheelchair_boarding` = accessible (**not available** while no stop is flagged, see below) |
| `accessToilets` | 10 | 8 | 8 | distance to the nearest accessible toilet (pram: or one with a changing table) |
| `rest` | 6 | 12 | 22 | distance to the nearest bench (limited mobility: full within 80 m, zero from 300 m) |
| `tactile` | 0 | 0 | 5 | distance to the nearest crossing with tactile paving |

Weights are planner-editable per profile (`PUT /api/safety/planner/weights`, keys as above). Citizen `PathHazard` reports lower the score of the Access layers (up to 30 points).

**No data is never accessible.** A square has a score only when it lies inside the downloaded area **and** at least one accessibility item is mapped within 250 m. Otherwise
there is no score: `-1` in the grid columns `access`, `accessPram`, `accessMobility` (`accessData` = 0), `score: null` / `hasData: false` / band `NoData` in the place card with a
`dataNoteCode` (`outside_area`, `no_data`, `unavailable`), `null` in route samples. Such squares are excluded from planner averages, gaps and rankings and are counted in the
`cellsNoData` KPI. Inside a scored square a missing tag still never counts as accessible: a footway without surface/slope data is not "smooth", a stop without
`wheelchair_boarding` and a toilet without a wheelchair tag are not accessible. A score built on fewer than 5 mapped items is flagged `thin_coverage`. If the dataset cannot be loaded
the model lists `access` in its data gaps.

**Accessible stops: no flagged stop, no punishment.** The ZTP GTFS feeds currently flag no stop as wheelchair-accessible (feeds A and M carry 0 = unknown, feed T has no flag). "No data is never accessible" still holds, but it must not punish every square, so when the data contains zero stops flagged accessible (or the stops dataset is unavailable) the `accessStops` factor is treated as **not available** for scoring: it is left out and the other factors' weights of each profile are rescaled at scoring time to add up to 100 again (the configured and planner weights are not changed). In the place card the factor has `hasData: false` and `dataNote` "no stop in the ZTP open data is flagged wheelchair-accessible" (shown as "no data", not "0 of N points"), the other factors carry the rescaled `weight`, no `REVIEW_ACCESSIBLE_STOPS` action or factor gap is produced from it, the planner KPI `noAccessibleStop400` is omitted and `conditions.dataGaps` contains `access_stops`. As soon as one stop is flagged the factor works as described above.
