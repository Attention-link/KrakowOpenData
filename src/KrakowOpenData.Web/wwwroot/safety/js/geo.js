// Addresses for points on the map. The API looks them up in OpenStreetMap (Photon); results are remembered on the device,
// so an address seen once is still shown with no connection. When nothing is known, callers show the coordinates.

import { h, icon } from './util.js';
import { isOffline } from './state.js';
import { geoReverse, ApiError } from './api.js';
import { kvGet, kvSet } from './db.js';
import { t } from './i18n.js';

const memory = new Map();
const keyOf = (lat, lon) => `${lat.toFixed(4)},${lon.toFixed(4)}`;

/** The address or place name nearest to a point (within ~300 m), or null. */
export async function reverseLabel(lat, lon) {
  const key = keyOf(lat, lon);
  if (memory.has(key)) return memory.get(key);
  const saved = await kvGet(`addr:${key}`);
  if (saved !== undefined && saved !== null) { memory.set(key, saved.label); return saved.label; }
  if (isOffline()) return null;
  try {
    const r = await geoReverse(lat, lon);
    memory.set(key, r.label);
    kvSet(`addr:${key}`, { label: r.label });
    return r.label;
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) { memory.set(key, null); return null; }
    return null; // service unavailable: show coordinates, try again next time
  }
}

export const coords = (lat, lon) => `${lat.toFixed(5)}, ${lon.toFixed(5)}`;

/**
 * An inline "address" line for a point: fills in the address when it arrives, coordinates meanwhile (and when none is known).
 * `fallback` (e.g. "Near Rynek stop") is used when the address service has nothing.
 */
export function addressLine(lat, lon, { fallback = null, bold = true } = {}) {
  const name = h('span', { class: bold ? 'addr-name' : '' }, fallback || t('addr.loading'));
  const small = h('span', { class: 'tiny muted num' }, ` ${coords(lat, lon)}`);
  const el = h('div', { class: 'addr' }, icon('pin', 'sm'), ' ', name, small);
  reverseLabel(lat, lon).then((label) => {
    name.textContent = label || fallback || t('addr.unknown');
  });
  el.update = (nlat, nlon) => {
    small.textContent = ` ${coords(nlat, nlon)}`;
    name.textContent = t('addr.loading');
    reverseLabel(nlat, nlon).then((label) => { name.textContent = label || fallback || t('addr.unknown'); });
  };
  return el;
}
