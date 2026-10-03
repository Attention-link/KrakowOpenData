import { describe, expect, it } from 'vitest';
import { bandRuns } from './route';
import type { CorridorSample, LatLon, RouteOption } from './types';

const sample = (safety: number): CorridorSample => ({ latitude: 0, longitude: 0, safety, heat: 100 - safety, combined: safety });

function route(path: LatLon[], safeties: number[]): RouteOption {
  return {
    kind: 'fastest', lengthMeters: 1000, walkingMinutes: 13, path, samples: safeties.map(sample),
    average: 50, worst: 10, weakestSampleIndex: 0, openReportsNearby: 0
  };
}

// A straight street, 10 equal segments.
const line: LatLon[] = Array.from({ length: 11 }, (_, i) => [50.06 + i * 0.001, 19.93] as LatLon);

describe('bandRuns', () => {
  it('gives one run when the whole way is in one band', () => {
    const runs = bandRuns(route(line, [90, 90, 90, 90]), 'safety', 'good');
    expect(runs).toHaveLength(1);
    expect(runs[0].band).toBe('Good');
    expect(runs[0].pts).toHaveLength(11);
  });

  it('cuts the path where the band changes, and the runs join up', () => {
    const runs = bandRuns(route(line, [90, 90, 20, 20]), 'safety', 'good');
    expect(runs.map((r) => r.band)).toEqual(['Good', 'Critical']);
    // The second run starts where the first one ends (no gap on the map).
    expect(runs[1].pts[0]).toEqual(runs[0].pts[runs[0].pts.length - 1]);
  });

  it('reads the heat score the other way round (a high heat score is bad)', () => {
    const r = route(line, [10, 10, 10, 10]);   // heat = 90 everywhere
    expect(bandRuns(r, 'heat', 'heat')[0].band).toBe('Critical');
    expect(bandRuns(r, 'safety', 'good')[0].band).toBe('Critical');
  });

  it('follows the bends: the colour is by distance along the path, not by number of points', () => {
    // Many points in the first half of the way, few in the second: the split is still halfway along the distance.
    const dense: LatLon[] = [...Array.from({ length: 20 }, (_, i) => [50.06 + i * 0.0001, 19.93] as LatLon), [50.07, 19.93]];
    const runs = bandRuns(route(dense, [90, 20]), 'safety', 'good');
    expect(runs.map((r) => r.band)).toEqual(['Good', 'Critical']);
  });

  it('returns nothing for an empty route', () => {
    expect(bandRuns(route([[50, 19]], [50]), 'safety', 'good')).toEqual([]);
    expect(bandRuns(route(line, []), 'safety', 'good')).toEqual([]);
  });
});
