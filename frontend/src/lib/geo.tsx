// Addresses for points on the map. The API looks them up in OpenStreetMap (Photon); results are remembered on the device,
// so an address seen once is still shown with no connection. When nothing is known, callers show the coordinates.

import { useEffect, useState } from 'react';
import { ApiError, geoReverse } from './api';
import { kvGet, kvSet } from './storage';
import { isOffline } from './store';
import { useT } from './i18n';
import { coords } from './util';
import { Icon } from '../components/ui';

const memory = new Map<string, string | null>();
const keyOf = (lat: number, lon: number) => `${lat.toFixed(4)},${lon.toFixed(4)}`;

/** The address or place name nearest to a point (within ~300 m), or null. */
export async function reverseLabel(lat: number, lon: number): Promise<string | null> {
  const key = keyOf(lat, lon);
  if (memory.has(key)) return memory.get(key) ?? null;
  const saved = await kvGet<{ label: string }>(`addr:${key}`);
  if (saved) { memory.set(key, saved.label); return saved.label; }
  if (isOffline()) return null;
  try {
    const r = await geoReverse(lat, lon);
    memory.set(key, r.label);
    void kvSet(`addr:${key}`, { label: r.label });
    return r.label;
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) { memory.set(key, null); return null; }
    return null; // service unavailable: show coordinates, try again next time
  }
}

/** The address of a point: undefined while loading, null when none is known. */
export function useAddress(lat: number, lon: number): string | null | undefined {
  const [label, setLabel] = useState<string | null | undefined>(undefined);
  useEffect(() => {
    let live = true;
    setLabel(undefined);
    void reverseLabel(lat, lon).then((l) => { if (live) setLabel(l); });
    return () => { live = false; };
  }, [lat, lon]);
  return label;
}

/** "Address" line for a point: the address when it arrives, `fallback` or a loading note meanwhile, with the coordinates. */
export function AddressLine({ lat, lon, fallback = null, bold = true }: { lat: number; lon: number; fallback?: string | null; bold?: boolean }) {
  const t = useT();
  const label = useAddress(lat, lon);
  const text = label === undefined ? (fallback || t('addr.loading')) : (label || fallback || t('addr.unknown'));
  return (
    <div className="addr">
      <Icon name="pin" size="sm" /> <span className={bold ? 'addr-name' : ''}>{text}</span>
      <span className="tiny muted num"> {coords(lat, lon)}</span>
    </div>
  );
}
