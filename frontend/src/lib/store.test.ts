import { beforeEach, describe, expect, it } from 'vitest';
import { appState, eventForMode, isOffline, setApp } from './store';

beforeEach(() => setApp({ online: true, apiOk: true, mode: null, me: null, plannerKey: null, dismissedAlerts: [] }));

describe('app state', () => {
  it('keeps what the user chose in localStorage', () => {
    setApp({ mode: 'heat' });
    expect(JSON.parse(localStorage.getItem('mode')!)).toBe('heat');
    setApp({ me: { lat: 50.06, lon: 19.94, source: 'manual' } });
    expect(JSON.parse(localStorage.getItem('me')!).lat).toBe(50.06);
    setApp({ me: null });
    expect(localStorage.getItem('me')).toBeNull();
  });

  it('keeps the planner key for the session only', () => {
    setApp({ plannerKey: 'secret' });
    expect(sessionStorage.getItem('plannerKey')).toBe('"secret"');
    expect(localStorage.getItem('plannerKey')).toBeNull();
    setApp({ plannerKey: null });
    expect(sessionStorage.getItem('plannerKey')).toBeNull();
  });

  it('remembers at most the last 50 dismissed alerts', () => {
    setApp({ dismissedAlerts: Array.from({ length: 60 }, (_, i) => `a${i}`) });
    const saved = JSON.parse(localStorage.getItem('dismissedAlerts')!);
    expect(saved).toHaveLength(50);
    expect(saved[49]).toBe('a59');
  });

  it('has a random anonymous device id', () => {
    expect(appState().deviceId).toMatch(/^dev-/);
  });
});

describe('offline detection', () => {
  it('is offline without a network or when the API does not answer', () => {
    expect(isOffline()).toBe(false);
    setApp({ online: false });
    expect(isOffline()).toBe(true);
    setApp({ online: true, apiOk: false });
    expect(isOffline()).toBe(true);
  });
});

describe('eventForMode', () => {
  it('maps the resident views to planning events', () => {
    expect(eventForMode('safety')).toBe('night');
    expect(eventForMode('heat')).toBe('heat');
    expect(eventForMode('both')).toBe('both');
  });
});
