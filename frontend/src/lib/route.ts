// Route helpers: cutting a route into runs of one score band so the colour follows the score along the street.

import { bandOf, type Kind } from './model';
import type { Band, CorridorSample, GridMeta, LatLon, RouteOption } from './types';
import { haversine } from './util';

export type ScoreKey = 'safety' | 'heat' | 'combined';
export interface BandRun { band: Band; pts: LatLon[] }

/**
 * The route path cut into runs of the same band. Each path segment takes the score of the sample at the same share of the
 * way (the samples are every ~50 m along the route), so the colours follow the streets, not a straight line.
 */
export function bandRuns(route: RouteOption, key: ScoreKey, kind: Kind, meta?: Pick<GridMeta, 'goodFrom' | 'fairFrom' | 'weakFrom'> | null): BandRun[] {
  const pts = route.path;
  const n = route.samples.length;
  if (pts.length < 2 || n === 0) return [];
  const cum = [0];
  for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + haversine(pts[i - 1], pts[i]));
  const total = cum[cum.length - 1] || 1;
  const runs: BandRun[] = [];
  let cur: BandRun | null = null;
  for (let i = 1; i < pts.length; i++) {
    const mid = (cum[i - 1] + cum[i]) / 2 / total;
    const s: CorridorSample = route.samples[Math.min(n - 1, Math.round(mid * (n - 1)))];
    const band = bandOf(s[key], meta, kind);
    if (cur && cur.band === band) cur.pts.push(pts[i]);
    else { cur = { band, pts: [pts[i - 1], pts[i]] }; runs.push(cur); }
  }
  return runs;
}

/** The score a sample has for a mode. */
export const SCORE_KEY = { safety: 'safety', heat: 'heat', both: 'combined' } as const;
