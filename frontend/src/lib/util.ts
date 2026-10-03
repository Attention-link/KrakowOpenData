import type { LatLon } from './types';

export const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

export function haversine(a: LatLon, b: LatLon): number {
  const R = 6371000;
  const rad = (d: number) => (d * Math.PI) / 180;
  const dLat = rad(b[0] - a[0]);
  const dLon = rad(b[1] - a[1]);
  const x = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a[0])) * Math.cos(rad(b[0])) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(x)));
}

export function formatDistance(m: number | null | undefined): string {
  if (m === null || m === undefined) return '–';
  return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${Math.round(m)} m`;
}

export const coords = (lat: number, lon: number) => `${lat.toFixed(5)}, ${lon.toFixed(5)}`;

export function uuid(): string {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
  const b = crypto.getRandomValues(new Uint8Array(16));
  return [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
}

type T = (key: string, params?: Record<string, string | number>) => string;

export function timeAgo(date: string | number, t: T, lang: string): string {
  const d = new Date(date);
  const s = Math.round((Date.now() - d.getTime()) / 1000);
  if (s < 45) return t('time.now');
  const m = Math.round(s / 60);
  if (m < 60) return t('time.minAgo', { n: m });
  const hrs = Math.round(m / 60);
  if (hrs < 36) return t('time.hAgo', { n: hrs });
  return new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short' }).format(d);
}

export function timeLeft(date: string, t: T): string {
  const m = Math.round((new Date(date).getTime() - Date.now()) / 60000);
  if (m <= 0) return t('time.expired');
  if (m < 90) return t('time.minLeft', { n: m });
  return t('time.hLeft', { n: Math.round(m / 60) });
}

export const geoLink = (lat: number, lon: number) => `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lon}#map=17/${lat}/${lon}`;

export const directionsLink = (lat: number, lon: number) =>
  `https://www.openstreetmap.org/directions?engine=fossgis_osrm_foot&route=%3B${lat}%2C${lon}`;

export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const ta = document.createElement('textarea');
    ta.style.cssText = 'position:fixed;opacity:0';
    ta.value = text;
    document.body.append(ta);
    ta.select();
    let ok = false;
    try { ok = document.execCommand('copy'); } catch { /* ignore */ }
    ta.remove();
    return ok;
  }
}

/** Announces a message to screen readers through the live region in index.html. */
export function announce(message: string) {
  const live = document.getElementById('live');
  if (!live) return;
  live.textContent = '';
  setTimeout(() => { live.textContent = message; }, 50);
}
