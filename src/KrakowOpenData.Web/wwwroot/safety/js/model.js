// Presentation helpers for the score grid. The SCORES are computed by the API (see SafetyModel in the back end);
// this file only reads them, colours them and finds things near a point. Band thresholds come from the API (grid.grid).
//
// DIRECTION OF THE SCORES: the safety score and the overall score are "higher = better". The HEAT score is "higher = hotter":
// 0 is a cool place, 100 a place with no way to cool down. Colours and bands always show good news in blue and bad news in
// red, so for heat the band is read from 100 − score. Pass kind = 'heat' wherever a heat score is coloured or banded.

import { haversine } from './util.js';

export const COL = { row: 0, col: 1, heat: 2, safety: 3, combined: 4, exposure: 5, reports: 6, priority: 7, flood: 8, air: 9 };

/**
 * The four score layers. mode = what residents choose; event = what planners and the API call it; api = the layer name in the
 * API (reports, alerts, factors). kind 'heat' = higher is hotter (colours and bands are read from 100 − score); otherwise higher = better.
 */
export const LAYERS = {
  safety: { mode: 'safety', event: 'night', api: 'Safety', icon: 'moon', kind: 'good', col: COL.safety },
  heat: { mode: 'heat', event: 'heat', api: 'Heat', icon: 'sun', kind: 'heat', col: COL.heat },
  flood: { mode: 'flood', event: 'flood', api: 'Flood', icon: 'wave', kind: 'good', col: COL.flood },
  air: { mode: 'air', event: 'air', api: 'Air', icon: 'wind', kind: 'good', col: COL.air }
};

/** Order of the tabs: night safety, heat, flood, air. */
export const MODE_KEYS = ['safety', 'heat', 'flood', 'air'];

/** A saved or suggested value turned into one of the four modes (anything else, such as the old "both", becomes night safety). */
export const asMode = (m) => (MODE_KEYS.includes(m) ? m : 'safety');

/** Planner event name of a mode, and back: night safety is "night" for the API. */
export const eventOfMode = (m) => LAYERS[asMode(m)].event;
export const modeOfEvent = (ev) => MODE_KEYS.find((k) => LAYERS[k].event === ev) || 'safety';

/** The layer key ('safety' ... 'air') of an API layer name such as 'Heat'; null for anything else. */
export const layerOfApi = (name) => MODE_KEYS.find((k) => LAYERS[k].api === name) || null;

export const BANDS = ['Critical', 'Weak', 'Fair', 'Good'];

const DEFAULT_THRESHOLDS = { GoodFrom: 75, FairFrom: 55, WeakFrom: 35 };

/** 'heat' for the heat layer / heat mode, otherwise 'good' (higher = better). */
export const kindOf = (layerOrMode) => (layerOrMode === 'heat' || layerOrMode === 'Heat' ? 'heat' : 'good');

/** A score turned so that higher is always better (a heat score of 80 is a goodness of 20). */
export const goodness = (score, kind = 'good') => (kind === 'heat' ? 100 - score : score);

export function bandOf(score, meta, kind = 'good') {
  const t = meta ? { GoodFrom: meta.goodFrom, FairFrom: meta.fairFrom, WeakFrom: meta.weakFrom } : DEFAULT_THRESHOLDS;
  const g = goodness(score, kind);
  return g >= t.GoodFrom ? 'Good' : g >= t.FairFrom ? 'Fair' : g >= t.WeakFrom ? 'Weak' : 'Critical';
}

/**
 * The score range a band covers, in the scale of the score. Safety/overall: Good = 75-100. Heat: Good = 0-25 (low heat).
 * Returns [from, to] of the displayed score.
 */
export function bandRange(band, meta, kind = 'good') {
  const g = meta ? { good: meta.goodFrom, fair: meta.fairFrom, weak: meta.weakFrom } : { good: 75, fair: 55, weak: 35 };
  const r = { Good: [g.good, 100], Fair: [g.fair, g.good], Weak: [g.weak, g.fair], Critical: [0, g.weak] }[band];
  return kind === 'heat' ? [100 - r[1], 100 - r[0]] : r;
}

export const BAND_FILL = { Good: '#2a78d6', Fair: '#9ec5f4', Weak: '#ee9b95', Critical: '#d03b3b' };

/** One-hue red ramp (light to dark) for planner priority: darker = act sooner. */
export const PRIORITY_RAMP = ['#fde0dd', '#f9b4ae', '#ee8d86', '#e0625c', '#d03b3b', '#a62b2b', '#7a1f1f'];

export function priorityColor(p) {
  const i = Math.min(PRIORITY_RAMP.length - 1, Math.floor((p / 100) * PRIORITY_RAMP.length));
  return PRIORITY_RAMP[i];
}

/** The score a mode shows: safety -> night safety, heat -> heat, both -> combined. */
export function scoreOf(row, mode) {
  return row[LAYERS[mode] ? LAYERS[mode].col : COL.combined];
}

export function indexGrid(grid) {
  const map = new Map();
  for (const r of grid.cells) map.set(`${r[0]}-${r[1]}`, r);
  return map;
}

export function cellOf(meta, lat, lon) {
  return {
    row: Math.floor((lat - meta.originLatitude) / meta.cellLatitudeDegrees),
    col: Math.floor((lon - meta.originLongitude) / meta.cellLongitudeDegrees)
  };
}

export function cellBounds(meta, row, col) {
  const s = meta.originLatitude + row * meta.cellLatitudeDegrees;
  const w = meta.originLongitude + col * meta.cellLongitudeDegrees;
  return [[s, w], [s + meta.cellLatitudeDegrees, w + meta.cellLongitudeDegrees]];
}

export function cellCenter(meta, row, col) {
  const [[s, w], [n, e]] = cellBounds(meta, row, col);
  return [(s + n) / 2, (w + e) / 2];
}

export const cellId = (row, col) => `${row}-${col}`;

/** Which feature kinds are shown, per mode, with their factor key, icon and colour family. */
export const RELIEF = {
  heat: ['water', 'green', 'refuge', 'toilets'],
  safety: ['openPlaces', 'nightTransit', 'aed'],
  flood: ['emergency'],
  air: ['green', 'refuge'],
  both: ['water', 'green', 'refuge', 'toilets', 'openPlaces', 'aed']
};

export const FACTOR_ICON = {
  water: 'water', green: 'tree', refuge: 'building', toilets: 'toilet', transit: 'bus',
  lighting: 'lamp', nightTransit: 'bus', openPlaces: 'shield', aed: 'heart',
  river: 'wave', emergency: 'shield', evacuation: 'bus', traffic: 'bus', trees: 'tree', cleanIndoor: 'building'
};

export const FACTOR_LAYER = {
  water: 'heat', green: 'heat', refuge: 'heat', toilets: 'heat', transit: 'heat',
  lighting: 'safety', nightTransit: 'safety', openPlaces: 'safety', aed: 'safety',
  river: 'flood', emergency: 'flood', evacuation: 'flood', traffic: 'air', trees: 'air', cleanIndoor: 'air'
};

/**
 * Nearest feature per key from the cached feature list (used offline). Distance to a park is measured to its edge,
 * like the API does. Returns an array of {key, kind, name, lat, lon, distance, minutes, hours}.
 */
export function nearestFromFeatures(features, lat, lon, keys, maxMeters = 1500) {
  const out = [];
  for (const key of keys) {
    let best = null;
    for (const f of features) {
      if (f.key !== key) continue;
      const d = Math.max(0, haversine([lat, lon], [f.latitude, f.longitude]) - (f.radiusMeters || 0));
      if (!best || d < best.distance) best = { f, distance: d };
    }
    if (best && best.distance <= maxMeters) {
      out.push({
        key, kind: best.f.kind, name: best.f.name, latitude: best.f.latitude, longitude: best.f.longitude,
        distanceMeters: Math.round(best.distance), walkingMinutes: Math.ceil(best.distance / 80), openingHours: best.f.openingHours
      });
    }
  }
  return out;
}

/** Which layer each report type feeds (mirrors ReportRules on the server). */
export const REPORT_LAYER = {
  LightOut: 'safety', UnsafeAtNight: 'safety', PathHazard: 'safety',
  WaterNotWorking: 'heat', NoShade: 'heat', HeatSpot: 'heat',
  FloodedStreet: 'flood', BlockedDrain: 'flood', RisingWater: 'flood',
  SmokeOrBurning: 'air', StrongFumes: 'air', DustCloud: 'air'
};
