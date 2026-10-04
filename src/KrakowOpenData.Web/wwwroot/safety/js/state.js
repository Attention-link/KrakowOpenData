// Shared app state with a tiny change-notification mechanism. Persistent bits live in localStorage.

import { ls, ss } from './db.js';
import { uuid } from './util.js';

const listeners = new Map();

function detectLang() {
  const l = (navigator.language || 'en').toLowerCase();
  return l.startsWith('pl') ? 'pl' : l.startsWith('uk') ? 'uk' : 'en';
}

function deviceId() {
  let id = ls.get('deviceId');
  if (!id) {
    id = `dev-${uuid()}`;
    ls.set('deviceId', id);
  }
  return id;
}

export const state = {
  lang: ls.get('lang') ?? detectLang(),
  mode: ['safety', 'heat', 'flood', 'air'].includes(ls.get('mode')) ? ls.get('mode') : null,   // 'safety' | 'heat' | 'flood' | 'air' (null until the user or the conditions choose)
  deviceId: deviceId(),                 // random, anonymous; only used to count supporters and presence
  online: navigator.onLine,
  apiOk: true,                          // false after a failed request; back to true when the API answers again
  me: ls.get('me'),                     // {lat, lon, source: 'gps' | 'manual'} the user's own area
  plannerKey: ss.get('plannerKey'),
  welcomed: ls.get('welcomed') === true,
  notify: ls.get('notify') === true,
  dismissedAlerts: ls.get('dismissedAlerts') ?? [],
  event: ['heat', 'night', 'flood', 'air'].includes(ls.get('plannerEvent')) ? ls.get('plannerEvent') : 'heat',
  theme: ls.get('theme') ?? 'auto'
};

const persisted = {
  lang: (v) => ls.set('lang', v),
  mode: (v) => ls.set('mode', v),
  me: (v) => (v ? ls.set('me', v) : ls.del('me')),
  welcomed: (v) => ls.set('welcomed', v),
  notify: (v) => ls.set('notify', v),
  dismissedAlerts: (v) => ls.set('dismissedAlerts', v.slice(-50)),
  event: (v) => ls.set('plannerEvent', v),
  theme: (v) => ls.set('theme', v),
  plannerKey: (v) => (v ? ss.set('plannerKey', v) : ss.del('plannerKey'))
};

export function set(patch) {
  for (const [k, v] of Object.entries(patch)) {
    if (state[k] === v) continue;
    state[k] = v;
    persisted[k]?.(v);
    emit(k, v);
  }
}

export function on(key, fn) {
  if (!listeners.has(key)) listeners.set(key, new Set());
  listeners.get(key).add(fn);
  return () => listeners.get(key).delete(fn);
}

export function emit(key, value) {
  listeners.get(key)?.forEach((fn) => { try { fn(value); } catch (e) { console.error(e); } });
}

/** True when live data cannot be fetched: no network, or the API does not answer. */
export const isOffline = () => !state.online || !state.apiOk;

export { eventOfMode as eventForMode } from './model.js';
