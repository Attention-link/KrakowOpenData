import { describe, expect, it } from 'vitest';
import { bandOf, bandRange, goodness, kindOf, nearestFromFeatures, cellOf, cellBounds } from './model';

describe('score bands', () => {
  it('reads a safety score as higher = better', () => {
    expect(bandOf(80)).toBe('Good');
    expect(bandOf(60)).toBe('Fair');
    expect(bandOf(40)).toBe('Weak');
    expect(bandOf(10)).toBe('Critical');
  });

  it('reads a heat score as higher = hotter', () => {
    expect(bandOf(10, null, 'heat')).toBe('Good');
    expect(bandOf(80, null, 'heat')).toBe('Critical');
    expect(goodness(80, 'heat')).toBe(20);
  });

  it('gives the heat ranges the other way round', () => {
    expect(bandRange('Good', null, 'heat')).toEqual([0, 25]);
    expect(bandRange('Critical', null, 'heat')).toEqual([65, 100]);
    expect(bandRange('Good')).toEqual([75, 100]);
  });

  it('knows which layer is heat', () => {
    expect(kindOf('heat')).toBe('heat');
    expect(kindOf('Heat')).toBe('heat');
    expect(kindOf('safety')).toBe('good');
  });
});

describe('grid maths', () => {
  const meta = { originLatitude: 49.9, originLongitude: 19.7, cellLatitudeDegrees: 0.002, cellLongitudeDegrees: 0.003, cellSizeMeters: 250, goodFrom: 75, fairFrom: 55, weakFrom: 35, searchRadiusMeters: 1500 };
  it('finds the cell of a point and its bounds', () => {
    const c = cellOf(meta, 50.0617, 19.9373);
    const [[s, w], [n, e]] = cellBounds(meta, c.row, c.col);
    expect(s).toBeLessThanOrEqual(50.0617);
    expect(n).toBeGreaterThan(50.0617);
    expect(w).toBeLessThanOrEqual(19.9373);
    expect(e).toBeGreaterThan(19.9373);
  });
});

describe('nearest features (offline)', () => {
  const features = [
    { key: 'water', kind: 'DrinkingWater', name: 'A', latitude: 50.0617, longitude: 19.9373, radiusMeters: 0, openingHours: null },
    { key: 'water', kind: 'DrinkingWater', name: 'B', latitude: 50.07, longitude: 19.95, radiusMeters: 0, openingHours: null },
    { key: 'green', kind: 'Park', name: 'P', latitude: 50.0617, longitude: 19.9473, radiusMeters: 500, openingHours: null }
  ];
  it('picks the nearest and measures parks to their edge', () => {
    const hits = nearestFromFeatures(features, 50.0617, 19.9375, ['water', 'green']);
    expect(hits.find((h) => h.key === 'water')?.name).toBe('A');
    expect(hits.find((h) => h.key === 'green')!.distanceMeters).toBeLessThan(400);
  });
  it('leaves out features beyond the search radius', () => {
    expect(nearestFromFeatures(features, 50.2, 20.2, ['water'])).toEqual([]);
  });
});
