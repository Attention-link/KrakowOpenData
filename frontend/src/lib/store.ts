// Shared app state (zustand). Persistent bits live in localStorage / sessionStorage under the same keys as the earlier version.

import { create } from 'zustand';
import { ls, ss } from './storage';
import { uuid } from './util';
import type { EventName, Lang, LatLon, Mode } from './types';

export interface Me { lat: number; lon: number; source: 'gps' | 'manual' }
export type Theme = 'auto' | 'light' | 'dark';

function detectLang(): Lang {
  const l = (navigator.language || 'en').toLowerCase();
  return l.startsWith('pl') ? 'pl' : l.startsWith('uk') ? 'uk' : 'en';
}

function deviceId(): string {
  let id = ls.get<string>('deviceId');
  if (!id) { id = `dev-${uuid()}`; ls.set('deviceId', id); }
  return id;
}

export interface AppState {
  lang: Lang;
  mode: Mode | null;            // null until the user or the conditions choose
  deviceId: string;             // random and anonymous: only used to count supporters and presence
  online: boolean;
  apiOk: boolean;               // false after a failed request; true again when the API answers
  me: Me | null;                // the user's own area
  plannerKey: string | null;    // session only
  welcomed: boolean;
  notify: boolean;
  dismissedAlerts: string[];
  event: EventName;             // what the planner plans for
  theme: Theme;
  set: (patch: Partial<Omit<AppState, 'set'>>) => void;
}

export const useApp = create<AppState>((set) => ({
  lang: ls.get<Lang>('lang') ?? detectLang(),
  mode: ls.get<Mode>('mode'),
  deviceId: deviceId(),
  online: navigator.onLine,
  apiOk: true,
  me: ls.get<Me>('me'),
  plannerKey: ss.get<string>('plannerKey'),
  welcomed: ls.get<boolean>('welcomed') === true,
  notify: ls.get<boolean>('notify') === true,
  dismissedAlerts: ls.get<string[]>('dismissedAlerts') ?? [],
  event: ls.get<EventName>('plannerEvent') ?? 'both',
  theme: ls.get<Theme>('theme') ?? 'auto',
  set: (patch) => set(patch)
}));

const persisted: Partial<Record<keyof AppState, (v: any) => void>> = {
  lang: (v) => ls.set('lang', v),
  mode: (v) => ls.set('mode', v),
  me: (v) => (v ? ls.set('me', v) : ls.del('me')),
  welcomed: (v) => ls.set('welcomed', v),
  notify: (v) => ls.set('notify', v),
  dismissedAlerts: (v: string[]) => ls.set('dismissedAlerts', v.slice(-50)),
  event: (v) => ls.set('plannerEvent', v),
  theme: (v) => ls.set('theme', v),
  plannerKey: (v) => (v ? ss.set('plannerKey', v) : ss.del('plannerKey'))
};

useApp.subscribe((state, prev) => {
  for (const key of Object.keys(persisted) as (keyof AppState)[]) {
    if (state[key] !== prev[key]) persisted[key]!(state[key]);
  }
});

export const appState = () => useApp.getState();
export const setApp = (patch: Partial<Omit<AppState, 'set'>>) => useApp.getState().set(patch);

/** True when live data cannot be fetched: no network, or the API does not answer. */
export const isOffline = () => { const s = useApp.getState(); return !s.online || !s.apiOk; };
export const useOffline = () => useApp((s) => !s.online || !s.apiOk);

export const eventForMode = (mode: Mode): EventName => (mode === 'safety' ? 'night' : mode === 'heat' ? 'heat' : 'both');

export type { LatLon };
