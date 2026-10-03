import { afterEach, describe, expect, it, vi } from 'vitest';
import { clamp, coords, directionsLink, formatDistance, geoLink, haversine, timeAgo, timeLeft, uuid } from './util';

const t = (key: string, p?: Record<string, string | number>) => (p ? `${key}:${Object.values(p).join(',')}` : key);

afterEach(() => vi.useRealTimers());

describe('formatDistance', () => {
  it('shows metres below a kilometre and kilometres above', () => {
    expect(formatDistance(0)).toBe('0 m');
    expect(formatDistance(149.6)).toBe('150 m');
    expect(formatDistance(999)).toBe('999 m');
    expect(formatDistance(1000)).toBe('1.0 km');
    expect(formatDistance(2540)).toBe('2.5 km');
  });
  it('shows a dash for nothing', () => {
    expect(formatDistance(null)).toBe('–');
    expect(formatDistance(undefined)).toBe('–');
  });
});

describe('haversine', () => {
  it('is zero for the same point and symmetric', () => {
    expect(haversine([50.06, 19.94], [50.06, 19.94])).toBe(0);
    const a = haversine([50.0617, 19.9373], [50.0647, 19.945]);
    expect(a).toBeCloseTo(haversine([50.0647, 19.945], [50.0617, 19.9373]), 6);
  });
  it('measures about 111 km per degree of latitude', () => {
    expect(haversine([50, 20], [51, 20])).toBeGreaterThan(110000);
    expect(haversine([50, 20], [51, 20])).toBeLessThan(112500);
  });
});

describe('clamp and coords', () => {
  it('keeps a value inside its range', () => {
    expect(clamp(5, 0, 10)).toBe(5);
    expect(clamp(-3, 0, 10)).toBe(0);
    expect(clamp(30, 0, 10)).toBe(10);
  });
  it('formats coordinates with five decimals', () => {
    expect(coords(50.061234567, 19.9)).toBe('50.06123, 19.90000');
  });
});

describe('timeAgo and timeLeft', () => {
  it('says "now" for the last seconds, then minutes, hours and a date', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-03T12:00:00Z'));
    expect(timeAgo(Date.now() - 10_000, t, 'en')).toBe('time.now');
    expect(timeAgo(Date.now() - 5 * 60_000, t, 'en')).toBe('time.minAgo:5');
    expect(timeAgo(Date.now() - 3 * 3_600_000, t, 'en')).toBe('time.hAgo:3');
    expect(timeAgo(Date.now() - 5 * 86_400_000, t, 'en')).toMatch(/Sep|28/);
  });
  it('counts down and then says expired', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-03T12:00:00Z'));
    expect(timeLeft(new Date(Date.now() + 30 * 60_000).toISOString(), t)).toBe('time.minLeft:30');
    expect(timeLeft(new Date(Date.now() + 5 * 3_600_000).toISOString(), t)).toBe('time.hLeft:5');
    expect(timeLeft(new Date(Date.now() - 1000).toISOString(), t)).toBe('time.expired');
  });
});

describe('links and ids', () => {
  it('builds map links', () => {
    expect(geoLink(50.1, 19.9)).toContain('mlat=50.1&mlon=19.9');
    expect(directionsLink(50.1, 19.9)).toContain('50.1%2C19.9');
  });
  it('makes different ids', () => {
    expect(uuid()).not.toBe(uuid());
  });
});
