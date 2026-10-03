// Presentation helpers for the score grid. The SCORES are computed by the API (see SafetyModel in the back end);
// this file only reads them, colours them and finds things near a point. Band thresholds come from the API (grid.grid).
//
// DIRECTION OF THE SCORES: the safety score and the overall score are "higher = better". The HEAT score is "higher = hotter":
// 0 is a cool place, 100 a place with no way to cool down. Colours and bands always show good news in blue and bad news in
// red, so for heat the band is read from 100 - score. Pass kind = 'heat' wherever a heat score is coloured or banded.

import type { Band, Feature, Grid, GridMeta, Mode } from './types';
import { haversine } from './util';

export const COL = { row: 0, col: 1, heat: 2, safety: 3, combined: 4, exposure: 5, reports: 6, priority: 7 } as const;
export const BANDS: Band[] = ['Critical', 'Weak', 'Fair', 'Good'];
export type Kind = 'heat' | 'good';

const DEFAULT_META = { goodFrom: 75, fairFrom: 55, weakFrom: 35 };

/** 'heat' for the heat layer / heat mode, otherwise 'good' (higher = better). */
export const kindOf = (layerOrMode: string): Kind => (layerOrMode === 'heat' || layerOrMode === 'Heat' ? 'heat' : 'good');

/** A score turned so that higher is always better (a heat score of 80 is a goodness of 20). */
export const goodness = (score: number, kind: Kind = 'good') => (kind === 'heat' ? 100 - score : score);

type Thresholds = Pick<GridMeta, 'goodFrom' | 'fairFrom' | 'weakFrom'>;

export function bandOf(score: number, meta?: Thresholds | null, kind: Kind = 'good'): Band {
  const m = meta ?? DEFAULT_META;
  const g = goodness(score, kind);
  return g >= m.goodFrom ? 'Good' : g >= m.fairFrom ? 'Fair' : g >= m.weakFrom ? 'Weak' : 'Critical';
}

/** The score range a band covers, in the scale of the score (heat: Good = 0-25). */
export function bandRange(band: Band, meta?: Thresholds | null, kind: Kind = 'good'): [number, number] {
  const m = meta ?? DEFAULT_META;
  const r: Record<Band, [number, number]> = {
    Good: [m.goodFrom, 100], Fair: [m.fairFrom, m.goodFrom], Weak: [m.weakFrom, m.fairFrom], Critical: [0, m.weakFrom]
  };
  const [a, b] = r[band];
  return kind === 'heat' ? [100 - b, 100 - a] : [a, b];
}

export const BAND_FILL: Record<Band, string> = { Good: '#2a78d6', Fair: '#9ec5f4', Weak: '#ee9b95', Critical: '#d03b3b' };

/** One-hue red ramp (light to dark) for planner priority: darker = act sooner. */
export const PRIORITY_RAMP = ['#fde0dd', '#f9b4ae', '#ee8d86', '#e0625c', '#d03b3b', '#a62b2b', '#7a1f1f'];

export function priorityColor(p: number): string {
  const i = Math.min(PRIORITY_RAMP.length - 1, Math.floor((p / 100) * PRIORITY_RAMP.length));
  return PRIORITY_RAMP[i];
}

/** The score a mode shows: safety -> night safety, heat -> heat, both -> combined. */
export function scoreOf(row: number[], mode: Mode): number {
  return row[mode === 'safety' ? COL.safety : mode === 'heat' ? COL.heat : COL.combined];
}

export function indexGrid(grid: Grid): Map<string, number[]> {
  const map = new Map<string, number[]>();
  for (const r of grid.cells) map.set(`${r[0]}-${r[1]}`, r);
  return map;
}

export function cellOf(meta: GridMeta, lat: number, lon: number) {
  return {
    row: Math.floor((lat - meta.originLatitude) / meta.cellLatitudeDegrees),
    col: Math.floor((lon - meta.originLongitude) / meta.cellLongitudeDegrees)
  };
}

export function cellBounds(meta: GridMeta, row: number, col: number): [[number, number], [number, number]] {
  const s = meta.originLatitude + row * meta.cellLatitudeDegrees;
  const w = meta.originLongitude + col * meta.cellLongitudeDegrees;
  return [[s, w], [s + meta.cellLatitudeDegrees, w + meta.cellLongitudeDegrees]];
}

export const cellId = (row: number, col: number) => `${row}-${col}`;

/** Which feature kinds are shown, per mode. */
export const RELIEF: Record<Mode, string[]> = {
  heat: ['water', 'green', 'refuge', 'toilets'],
  safety: ['openPlaces', 'nightTransit', 'aed'],
  both: ['water', 'green', 'refuge', 'toilets', 'openPlaces', 'aed']
};

export const FACTOR_ICON: Record<string, string> = {
  water: 'water', green: 'tree', refuge: 'building', toilets: 'toilet', transit: 'bus',
  lighting: 'lamp', nightTransit: 'bus', openPlaces: 'shield', aed: 'heart'
};

export const FACTOR_LAYER: Record<string, 'heat' | 'safety'> = {
  water: 'heat', green: 'heat', refuge: 'heat', toilets: 'heat', transit: 'heat',
  lighting: 'safety', nightTransit: 'safety', openPlaces: 'safety', aed: 'safety'
};

export interface NearHit {
  key: string; kind: string; name: string | null; latitude: number; longitude: number;
  distanceMeters: number; walkingMinutes: number; openingHours: string | null;
}

/** Nearest feature per key from the cached feature list (used offline). Distance to a park is measured to its edge. */
export function nearestFromFeatures(features: Feature[], lat: number, lon: number, keys: string[], maxMeters = 1500): NearHit[] {
  const out: NearHit[] = [];
  for (const key of keys) {
    let best: { f: Feature; distance: number } | null = null;
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
