// Leaflet in React: <MapView> creates the map; layer components (<ScoreGrid>, <Marker>, <Route>, <Places>, ...) add and remove
// their Leaflet layers as they mount and unmount. Leaflet itself is bundled, so the map shell also works offline
// (tiles you have seen come from the service worker cache, the score grid is drawn from saved data).

import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { Icon } from '../components/ui';
import { BAND_FILL, FACTOR_ICON, FACTOR_LAYER, bandOf, cellBounds, cellOf, indexGrid, kindOf, priorityColor, scoreOf } from '../lib/model';
import { t } from '../lib/i18n';
import type { Band, Feature, Grid, LatLon, Mode } from '../lib/types';

export const KRAKOW: LatLon = [50.0617, 19.9373];
const MAX_BOUNDS: L.LatLngBoundsExpression = [[49.88, 19.68], [50.2, 20.32]];

export const MapCtx = createContext<L.Map | null>(null);
export const useMap = () => useContext(MapCtx);

interface MapViewProps {
  center?: LatLon;
  zoom?: number;
  className?: string;
  label?: string;
  zoomControl?: boolean;
  onReady?: (map: L.Map) => void;
  children?: ReactNode;
}

export function MapView({ center = KRAKOW, zoom = 13, className = '', label, zoomControl = true, onReady, children }: MapViewProps) {
  const el = useRef<HTMLDivElement>(null);
  const [map, setMap] = useState<L.Map | null>(null);
  const instance = useRef<L.Map | null>(null);
  const teardown = useRef<ReturnType<typeof setTimeout>>();

  useEffect(() => {
    // React (StrictMode in development) may unmount and mount again straight away. The map is only destroyed when no
    // mount follows, so layers are never added to a map that has been removed.
    clearTimeout(teardown.current);
    let m = instance.current;
    if (!m) {
      m = L.map(el.current!, {
        center, zoom, minZoom: 10, maxZoom: 19, zoomControl: false, preferCanvas: true, maxBounds: MAX_BOUNDS, maxBoundsViscosity: 0.6
      });
      if (zoomControl) L.control.zoom({ position: 'bottomright' }).addTo(m);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
      }).addTo(m);
      instance.current = m;
      onReady?.(m);
    }
    // The container changes size with the bottom sheet, the window and orientation.
    const map0 = m;
    const ro = new ResizeObserver(() => map0.invalidateSize({ pan: false }));
    ro.observe(el.current!);
    setMap(m);
    return () => {
      ro.disconnect();
      setMap(null);
      teardown.current = setTimeout(() => { instance.current?.remove(); instance.current = null; }, 0);
    };
    // The map is created once; center/zoom are only its starting view.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className={`map-wrap ${className}`.trim()}>
      <div ref={el} className="map" role="application" aria-label={label} />
      {map && <MapCtx.Provider value={map}>{children}</MapCtx.Provider>}
    </div>
  );
}

/** Adds a Leaflet layer to the map while mounted. `make` runs again when `deps` change. */
export function useLayer<T extends L.Layer>(make: (map: L.Map) => T | null, deps: unknown[]): T | null {
  const map = useMap();
  const [layer, setLayer] = useState<T | null>(null);
  useEffect(() => {
    if (!map) return;
    const l = make(map);
    if (l) l.addTo(map);
    setLayer(l);
    return () => { l?.remove(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, ...deps]);
  return layer;
}

// ── Markers ──────────────────────────────────────────────────────────────────
const html = (node: ReactNode) => renderToStaticMarkup(<>{node}</>);

// Icons are cached: a layer is rebuilt when its icon object changes, and a new object on every render would rebuild every marker.
const iconCache = new Map<string, L.DivIcon>();
const cached = (key: string, make: () => L.DivIcon) => { let i = iconCache.get(key); if (!i) { i = make(); iconCache.set(key, i); } return i; };

export function dotIcon(icon: string | null, kind: '' | 'safety' | 'heat' | 'me', small = false) {
  return cached(`dot|${icon}|${kind}|${small}`, () => L.divIcon({
    html: html(<div className={`dot-marker ${kind} ${small ? 'small' : ''}`.trim()}>{icon && <Icon name={icon} size="sm" />}</div>),
    className: '', iconSize: small ? [22, 22] : [26, 26], iconAnchor: small ? [11, 11] : [13, 13]
  }));
}

export function pinIcon(icon = 'pin') {
  return cached(`pin|${icon}`, () => L.divIcon({ html: html(<div className="pin"><Icon name={icon} /></div>), className: '', iconSize: [34, 34], iconAnchor: [17, 34] }));
}

/** A clear start (green "A") or destination (red pin "B") marker with a text tag. */
export function endIcon(which: 'start' | 'end', label: string) {
  return cached(`end|${which}|${label}`, () => L.divIcon({
    html: html(<div className={`end-marker ${which}`}><div className="badge"><span>{which === 'start' ? 'A' : 'B'}</span></div><div className="tag">{label}</div></div>),
    className: '', iconSize: [0, 0], iconAnchor: [0, 0]
  }));
}

interface MarkerProps {
  position: LatLon;
  icon: L.DivIcon;
  title?: string;
  draggable?: boolean;
  z?: number;
  tooltip?: string;
  onDrag?: (p: LatLon) => void;
  onDragEnd?: (p: LatLon) => void;
  onClick?: () => void;
}

export function Marker({ position, icon, title, draggable, z = 0, tooltip, onDrag, onDragEnd, onClick }: MarkerProps) {
  const cb = useRef({ onDrag, onDragEnd, onClick });
  cb.current = { onDrag, onDragEnd, onClick };
  const marker = useLayer(() => {
    const m = L.marker(position, { icon, title, draggable, zIndexOffset: z, keyboard: false });
    if (tooltip) m.bindTooltip(tooltip);
    m.on('drag', () => { const p = m.getLatLng(); cb.current.onDrag?.([p.lat, p.lng]); });
    m.on('dragend', () => { const p = m.getLatLng(); cb.current.onDragEnd?.([p.lat, p.lng]); });
    m.on('click', () => cb.current.onClick?.());
    return m;
  }, [icon, title, draggable, z, tooltip]);
  // Moving a marker must not recreate it (that would cancel a drag in progress).
  useEffect(() => { marker?.setLatLng(position); }, [marker, position[0], position[1]]);
  return null;
}

// ── Lines, circles, rectangles ───────────────────────────────────────────────
export function Polyline({ positions, options, tooltip }: { positions: LatLon[]; options: L.PolylineOptions; tooltip?: string }) {
  useLayer(() => {
    const l = L.polyline(positions, options);
    if (tooltip) l.bindTooltip(tooltip, { sticky: true });
    return l;
  }, [positions, JSON.stringify(options), tooltip]);
  return null;
}

export function Circle({ center, radius, options, tooltip }: { center: LatLon; radius: number; options?: L.CircleOptions; tooltip?: string }) {
  const c = useLayer(() => {
    const l = L.circle(center, { radius, ...options });
    if (tooltip) l.bindTooltip(tooltip, { direction: 'top' });
    return l;
  }, [JSON.stringify(options), tooltip]);
  useEffect(() => { c?.setLatLng(center); c?.setRadius(radius); }, [c, center[0], center[1], radius]);
  return null;
}

export function Rect({ bounds, options }: { bounds: L.LatLngBoundsExpression; options: L.PathOptions }) {
  useLayer(() => L.rectangle(bounds, options), [JSON.stringify(bounds), JSON.stringify(options)]);
  return null;
}

/** Moves the map to fit these points whenever `fitKey` changes. */
export function FitBounds({ points, fitKey, maxZoom = 17, pad = 0.3 }: { points: LatLon[]; fitKey: unknown; maxZoom?: number; pad?: number }) {
  const map = useMap();
  useEffect(() => {
    if (map && points.length) map.fitBounds(L.latLngBounds(points).pad(pad), { maxZoom });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, fitKey]);
  return null;
}

/** Calls `onClick` for clicks on the map itself. */
export function MapClick({ onClick, enabled = true }: { onClick: (p: LatLon) => void; enabled?: boolean }) {
  const map = useMap();
  const ref = useRef(onClick);
  ref.current = onClick;
  useEffect(() => {
    if (!map || !enabled) return;
    const fn = (e: L.LeafletMouseEvent) => ref.current([e.latlng.lat, e.latlng.lng]);
    map.on('click', fn);
    return () => { map.off('click', fn); };
  }, [map, enabled]);
  return null;
}

// ── The score grid: one canvas rectangle per cell ───────────────────────────
interface ScoreGridProps {
  grid: Grid | null;
  mode: Mode;
  style?: 'score' | 'priority';
  selected?: { row: number; col: number } | null;
  /** Called for every click on the map with the cell under it (row is null outside the scored area). */
  onPick?: (p: LatLon, cell: { row: number; col: number }, row: number[] | null) => void;
  hoverText?: (row: number[]) => string;
}

/** Clicks and hover are resolved from the cell maths, not per-rectangle events, so thousands of cells stay fast. */
export function ScoreGrid({ grid, mode, style = 'score', selected, onPick, hoverText }: ScoreGridProps) {
  const map = useMap();
  const index = useRef<Map<string, number[]>>(new Map());

  useLayer((m) => {
    if (!grid) return null;
    index.current = indexGrid(grid);
    const group = L.layerGroup();
    const renderer = L.canvas({ padding: 0.3 });
    for (const r of grid.cells) {
      const score = style === 'priority' ? r[7] : scoreOf(r, mode);
      const fill = style === 'priority' ? priorityColor(score) : BAND_FILL[bandOf(score, grid.grid, kindOf(mode))];
      L.rectangle(cellBounds(grid.grid, r[0], r[1]), {
        renderer, stroke: false, fillColor: fill, fillOpacity: style === 'priority' ? Math.min(0.75, 0.25 + score / 160) : 0.42, interactive: false
      }).addTo(group);
    }
    return group as unknown as L.Layer;
  }, [grid, mode, style]);

  const cb = useRef({ onPick, hoverText, grid });
  cb.current = { onPick, hoverText, grid };

  useEffect(() => {
    if (!map) return;
    const tip = L.tooltip({ direction: 'top', className: 'grid-tip', sticky: false, opacity: 0.95 });
    const rowAt = (e: L.LeafletMouseEvent) => {
      const g = cb.current.grid;
      if (!g) return null;
      const c = cellOf(g.grid, e.latlng.lat, e.latlng.lng);
      return { c, row: index.current.get(`${c.row}-${c.col}`) ?? null };
    };
    const click = (e: L.LeafletMouseEvent) => {
      const hit = rowAt(e);
      if (hit) cb.current.onPick?.([e.latlng.lat, e.latlng.lng], hit.c, hit.row);
    };
    const move = (e: L.LeafletMouseEvent) => {
      if (!cb.current.hoverText || window.matchMedia('(pointer: coarse)').matches) return;
      const hit = rowAt(e);
      if (!hit?.row) { tip.remove(); return; }
      tip.setLatLng(e.latlng).setContent(cb.current.hoverText(hit.row)).addTo(map);
    };
    const out = () => tip.remove();
    map.on('click', click); map.on('mousemove', move); map.on('mouseout', out);
    return () => { map.off('click', click); map.off('mousemove', move); map.off('mouseout', out); tip.remove(); };
  }, [map]);

  // The outline of the selected square.
  useLayer(() => {
    if (!grid || !selected) return null;
    return L.rectangle(cellBounds(grid.grid, selected.row, selected.col), { color: '#17171a', weight: 2, fill: false, interactive: false, dashArray: '4 3' });
  }, [grid, selected?.row, selected?.col]);

  return null;
}

// ── Places that feed the scores (water, parks, toilets, refuges, night-open places, defibrillators) ──
interface PlacesProps { features: Feature[] | null; keys: string[]; enabled: boolean; onWalk?: (f: Feature) => void }

function popupFor(f: Feature, onWalk?: (f: Feature) => void) {
  const root = document.createElement('div');
  const title = document.createElement('div');
  title.className = 'poi-title';
  title.textContent = f.name || t(`kind.${f.kind}`);
  const sub = document.createElement('div');
  sub.className = 'poi-sub';
  sub.textContent = `${t(`kind.${f.kind}`)} · ${t(`factor.${f.key}`)}`;
  root.append(title, sub);
  if (f.openingHours) {
    const h = document.createElement('div');
    h.className = 'poi-sub';
    h.textContent = `${t('map.hours')}: ${f.openingHours}`;
    root.append(h);
  }
  if (onWalk) {
    const b = document.createElement('button');
    b.className = 'btn sm';
    b.type = 'button';
    b.textContent = t('map.walkHere');
    b.addEventListener('click', () => onWalk(f));
    root.append(b);
  }
  return root;
}

/** Draws the mapped places in view as small icons. Needs zoom 14 or more. */
export function Places({ features, keys, enabled, onWalk }: PlacesProps) {
  const map = useMap();
  const [tick, setTick] = useState(0);
  useEffect(() => {
    if (!map) return;
    const fn = () => setTick((n) => n + 1);
    map.on('moveend', fn);
    return () => { map.off('moveend', fn); };
  }, [map]);

  const keyList = keys.join(',');
  useLayer((m) => {
    if (!enabled || !features || m.getZoom() < 14) return null;
    const group = L.layerGroup();
    const set = new Set(keys);
    const bounds = m.getBounds().pad(0.1);
    let n = 0;
    for (const f of features) {
      if (!set.has(f.key) || !bounds.contains([f.latitude, f.longitude])) continue;
      if (++n > 400) break;
      const layer = FACTOR_LAYER[f.key] || 'heat';
      if (f.key === 'green' && f.radiusMeters > 20) {
        L.circle([f.latitude, f.longitude], { radius: Math.min(f.radiusMeters, 400), color: '#2f8f4e', weight: 1, opacity: 0.5, fillColor: '#2f8f4e', fillOpacity: 0.12, interactive: false }).addTo(group);
      }
      const icon = L.divIcon({
        html: html(<div className={`poi ${layer}`}><Icon name={FACTOR_ICON[f.key] || 'pin'} size="sm" /></div>),
        className: '', iconSize: [22, 22], iconAnchor: [11, 11]
      });
      const marker = L.marker([f.latitude, f.longitude], { icon, title: f.name || t(`kind.${f.kind}`), keyboard: false });
      marker.bindPopup(() => popupFor(f, (x) => { m.closePopup(); onWalk?.(x); }), { minWidth: 160 });
      marker.addTo(group);
    }
    return group as unknown as L.Layer;
  }, [features, keyList, enabled, tick]);
  return null;
}

export type { Band };
