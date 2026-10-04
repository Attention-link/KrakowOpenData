// Leaflet wrapper: base map, the score grid drawn on a canvas, and small marker helpers.
// The grid is drawn from cached data, so the map still works with no tiles and no connection (grey background).

import { cellBounds, cellOf, bandOf, kindOf, BAND_FILL, priorityColor, scoreOf, COL, indexGrid, FACTOR_ICON, FACTOR_LAYER } from './model.js';
import { h, icon, clear, formatDistance, reducedMotion } from './util.js';
import { addressLine } from './geo.js';
import { t } from './i18n.js';

export const KRAKOW = [50.0617, 19.9373];
const MAX_BOUNDS = [[49.88, 19.68], [50.2, 20.32]];

export function createMap(el, { center = KRAKOW, zoom = 13 } = {}) {
  const still = reducedMotion();
  const map = L.map(el, {
    center, zoom, minZoom: 10, maxZoom: 19, zoomControl: false, preferCanvas: true,
    maxBounds: MAX_BOUNDS, maxBoundsViscosity: 0.6, attributionControl: true,
    ...(still ? { zoomAnimation: false, fadeAnimation: false, markerZoomAnimation: false, inertia: false } : {})
  });
  // Reduced motion: flying and panning jump straight to the target (panTo and setView pan through panBy).
  if (still) {
    const panBy = map.panBy.bind(map);
    map.panBy = (offset, options) => panBy(offset, { ...options, animate: false });
    map.flyTo = (latlng, z, options) => map.setView(latlng, z, { ...options, animate: false });
    map.flyToBounds = (bounds, options) => map.fitBounds(bounds, { ...options, animate: false });
  }
  L.control.zoom({ position: 'bottomright' }).addTo(map);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
    maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
  }).addTo(map);
  return map;
}

/** A div marker with an icon inside. kind: '' | 'safety' | 'heat' | 'me'. */
export function iconMarker(latlng, iconName, kind = '', opts = {}) {
  const wrap = h('div', { class: `dot-marker ${kind} ${opts.small ? 'small' : ''}` }, iconName ? icon(iconName, 'sm') : null);
  const html = wrap.outerHTML;
  return L.marker(latlng, {
    icon: L.divIcon({ html, className: '', iconSize: opts.small ? [22, 22] : [26, 26], iconAnchor: opts.small ? [11, 11] : [13, 13] }),
    keyboard: !!opts.keyboard, title: opts.title, zIndexOffset: opts.z || 0
  });
}

export function pinMarker(latlng, opts = {}) {
  const wrap = h('div', { class: 'pin' }, icon(opts.icon || 'pin'));
  return L.marker(latlng, {
    icon: L.divIcon({ html: wrap.outerHTML, className: '', iconSize: [34, 34], iconAnchor: [17, 34] }),
    draggable: !!opts.draggable, title: opts.title, zIndexOffset: 1000
  });
}

let hintCount = 0;
/**
 * Keyboard use of a map (Leaflet already pans with the arrows and zooms with + / −): a hint read with the map, and
 * Enter / Space "clicks" the centre of the map, so the grid and the report pin answer it exactly as they answer a click
 * there. A crosshair marks the centre while the map has keyboard focus (css: .map:focus-visible). Returns a cleanup.
 */
export function keyboardPick(map, hint) {
  const el = map.getContainer();
  const id = `map-hint-${++hintCount}`;
  const note = h('p', { id, class: 'sr-only' }, hint);
  el.after(note);
  el.setAttribute('aria-describedby', id);
  if (el.tabIndex < 0) el.tabIndex = 0;
  const onKey = (e) => {
    if (e.target !== el || (e.key !== 'Enter' && e.key !== ' ')) return;
    e.preventDefault();
    const latlng = map.getCenter();
    map.fire('click', { latlng, layerPoint: map.latLngToLayerPoint(latlng), containerPoint: map.latLngToContainerPoint(latlng), originalEvent: e });
  };
  el.addEventListener('keydown', onKey);
  return () => { el.removeEventListener('keydown', onKey); note.remove(); };
}

/**
 * Draws the score grid: one canvas rectangle per cell. `style` is 'score' (blue-red bands by mode) or 'priority' (red ramp).
 * Clicks and hover are resolved from the cell math, not per-rectangle events, so thousands of cells stay fast.
 */
export class GridLayer {
  constructor(map, { onSelect, onHover } = {}) {
    this.map = map;
    this.group = L.layerGroup().addTo(map);
    this.renderer = L.canvas({ padding: 0.3 });
    this.selected = null;
    this.onSelect = onSelect;
    this.hoverTip = L.tooltip({ direction: 'top', className: 'grid-tip', sticky: false, opacity: 0.95 });
    map.on('click', (e) => {
      if (!this.grid) return;
      const c = cellOf(this.grid.grid, e.latlng.lat, e.latlng.lng);
      onSelect?.(e.latlng, c, this.index?.get(`${c.row}-${c.col}`) || null);
    });
    if (onHover) {
      map.on('mousemove', (e) => {
        if (!this.grid) return;
        const c = cellOf(this.grid.grid, e.latlng.lat, e.latlng.lng);
        onHover(e.latlng, this.index?.get(`${c.row}-${c.col}`) || null, this.hoverTip);
      });
      map.on('mouseout', () => this.hoverTip.remove());
    }
  }

  /** grid: the API's GridDto. opts: {mode: 'safety'|'heat'|'both', style: 'score'|'priority', opacity} */
  draw(grid, { mode = 'both', style = 'score', opacity = 0.62 } = {}) {
    this.grid = grid;
    this.index = indexGrid(grid);
    this.group.clearLayers();
    this.rects = new Map();
    for (const r of grid.cells) {
      const score = style === 'priority' ? r[COL.priority] : scoreOf(r, mode);
      const fill = style === 'priority' ? priorityColor(score) : BAND_FILL[bandOf(score, grid.grid, kindOf(mode))];
      const rect = L.rectangle(cellBounds(grid.grid, r[COL.row], r[COL.col]), {
        renderer: this.renderer, stroke: false, fillColor: fill, fillOpacity: style === 'priority' ? Math.min(0.75, 0.25 + score / 160) : opacity, interactive: false
      });
      rect.addTo(this.group);
      this.rects.set(`${r[COL.row]}-${r[COL.col]}`, rect);
    }
  }

  /** Outlines one cell. Pass null to clear. */
  select(row, col) {
    if (this.outline) { this.outline.remove(); this.outline = null; }
    if (row === null || !this.grid) return;
    this.outline = L.rectangle(cellBounds(this.grid.grid, row, col), {
      renderer: this.renderer, color: '#17171a', weight: 2, fill: false, interactive: false, dashArray: '4 3'
    }).addTo(this.map);
  }

  destroy() {
    this.group.remove();
    this.outline?.remove();
    this.hoverTip.remove();
  }
}

/** Leaflet needs a size recalculation when its container resizes (sheet drag, orientation change). */
export function watchResize(map, el) {
  const ro = new ResizeObserver(() => map.invalidateSize({ pan: false }));
  ro.observe(el);
  return () => ro.disconnect();
}

/**
 * The label every map shows for what it is looking at: the address (looked up from the coordinates) and the radius covered.
 * Put the returned element inside the map's overlay area, then call show({lat, lon, radiusMeters, radiusText, fallback, extra}).
 */
export function mapInfo() {
  const el = h('div', { class: 'map-info', hidden: true, role: 'status' });
  return {
    el,
    show({ lat, lon, radiusMeters, radiusText, fallback, extra }) {
      clear(el);
      el.append(addressLine(lat, lon, { fallback }));
      const radius = radiusText || (radiusMeters ? t('cov.radius', { r: formatDistance(radiusMeters) }) : null);
      if (radius) el.append(h('div', { class: 'tiny' }, icon('target', 'sm'), ' ', radius));
      if (extra) el.append(h('div', { class: 'tiny muted' }, extra));
      el.hidden = false;
    },
    showText(text, sub) {
      clear(el);
      el.append(h('div', { class: 'addr' }, icon('map', 'sm'), ' ', h('span', { class: 'addr-name' }, text)));
      if (sub) el.append(h('div', { class: 'tiny muted' }, sub));
      el.hidden = false;
    },
    hide() { el.hidden = true; clear(el); }
  };
}

/** A dashed circle showing how far a lookup reaches, with a tooltip. */
export function coverageCircle(latlng, radiusMeters, label) {
  const c = L.circle(latlng, { radius: radiusMeters, color: '#17171a', weight: 1.5, opacity: 0.7, dashArray: '6 6', fillColor: '#17171a', fillOpacity: 0.03, interactive: false });
  if (label) c.bindTooltip(label, { permanent: false, direction: 'top' });
  return c;
}

// ── Start and destination markers ────────────────────────────────────────────
/** A clear start (green "A") or destination (red pin "B") marker with a text tag. which: 'start' | 'end'. */
export function endMarker(latlng, which, label) {
  const letter = which === 'start' ? 'A' : 'B';
  const el = h('div', { class: `end-marker ${which}` },
    h('div', { class: 'badge' }, h('span', null, letter)),
    h('div', { class: 'tag' }, label));
  return L.marker(latlng, {
    icon: L.divIcon({ html: el.outerHTML, className: '', iconSize: [0, 0], iconAnchor: [0, 0] }),
    zIndexOffset: 1200, keyboard: false, title: `${label}: ${letter}`
  });
}

// ── Places on the map (water, parks, toilets, refuges, night-open places, defibrillators) ──
/**
 * Draws the mapped places that feed the scores as small icons, for the part of the map in view. Needs zoom 14 or more.
 * getFeatures() returns the API's feature list; getKeys() the factor keys to show (depends on the view); onWalk(feature) is optional.
 */
export class PlacesLayer {
  constructor(map, { getFeatures, getKeys, onWalk }) {
    this.map = map;
    this.group = L.layerGroup().addTo(map);
    this.getFeatures = getFeatures;
    this.getKeys = getKeys;
    this.onWalk = onWalk;
    this.enabled = true;
    this.redraw = () => this.draw();
    map.on('moveend', this.redraw);
  }

  setEnabled(on) {
    this.enabled = on;
    this.draw();
  }

  draw() {
    this.group.clearLayers();
    if (!this.enabled || this.map.getZoom() < 14) return;
    const features = this.getFeatures() || [];
    const keys = new Set(this.getKeys());
    const bounds = this.map.getBounds().pad(0.1);
    let n = 0;
    for (const f of features) {
      if (!keys.has(f.key) || !bounds.contains([f.latitude, f.longitude])) continue;
      if (++n > 400) break;
      const layer = FACTOR_LAYER[f.key] || 'heat';
      if (f.key === 'green' && f.radiusMeters > 20) {
        L.circle([f.latitude, f.longitude], { radius: Math.min(f.radiusMeters, 400), color: '#2f8f4e', weight: 1, opacity: 0.5, fillColor: '#2f8f4e', fillOpacity: 0.12, interactive: false }).addTo(this.group);
      }
      const dot = h('div', { class: `poi ${layer}` }, icon(FACTOR_ICON[f.key] || 'pin', 'sm'));
      const m = L.marker([f.latitude, f.longitude], {
        icon: L.divIcon({ html: dot.outerHTML, className: '', iconSize: [22, 22], iconAnchor: [11, 11] }),
        title: f.name || t(`kind.${f.kind}`), keyboard: true   // Tab reaches each place, Enter opens its popup
      });
      m.bindPopup(() => this.popup(f), { minWidth: 160 });
      m.addTo(this.group);
    }
  }

  popup(f) {
    return h('div', null,
      h('div', { class: 'poi-title' }, f.name || t(`kind.${f.kind}`)),
      h('div', { class: 'poi-sub' }, `${t(`kind.${f.kind}`)} · ${t(`factor.${f.key}`)}`),
      f.openingHours ? h('div', { class: 'poi-sub' }, `${t('map.hours')}: ${f.openingHours}`) : null,
      this.onWalk ? h('button', { class: 'btn sm', type: 'button', onclick: () => { this.map.closePopup(); this.onWalk(f); } }, icon('walk', 'sm'), t('map.walkHere')) : null);
  }

  destroy() {
    this.map.off('moveend', this.redraw);
    this.group.remove();
  }
}
