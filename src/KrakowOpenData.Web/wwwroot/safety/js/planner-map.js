// Planner map: the whole city coloured by priority (where to act first) or by score, with resident reports and
// active alert areas on top. Click any square to open its drill-down drawer.

import { h, icon, clear, toast } from './util.js';
import { t } from './i18n.js';
import { P, pOn, loadGridFor, loadReports, loadAlerts, openCellDrawer, openAlertDialog, staleBanner, closeDrawer } from './planner-common.js';
import { createMap, GridLayer, PlacesLayer, iconMarker, watchResize, mapInfo, KRAKOW, keyboardPick } from './map.js';
import { legendBody, loadMethod, openMethod, openScoreExplainer, infoButton, bandText } from './explain.js';
import { layerMode, layerDef, apiLayerOf, reportLayerOf, noDataSwatch, openAccessScoreExplainer, openNoDataExplainer } from './planner-access.js';
import { cachedGet, getFeatures } from './api.js';
import { BAND_FILL, PRIORITY_RAMP, COL, RELIEF, cellId, cellBounds, indexGrid, bandOf, bandRange, priorityColor, scoreOf, noDataRow, applyColumns } from './model.js';

/** Neutral grey of a square with no accessibility data: not a band colour, never good and never critical. */
const NO_DATA_FILL = '#8a8f98';

/**
 * The grid for the planner. For the accessibility layer it colours each square with the score of the chosen profile and draws the squares with
 * no data in neutral grey; every other layer is drawn by the shared GridLayer.
 */
class PlannerGrid extends GridLayer {
  draw(grid, opts = {}) {
    if (opts.mode !== 'access') return super.draw(grid, opts);
    applyColumns(grid.columns);
    this.grid = grid;
    this.index = indexGrid(grid);
    this.group.clearLayers();
    this.rects = new Map();
    for (const r of grid.cells) {
      const none = noDataRow(r, 'access', opts.profile);
      const value = opts.style === 'priority' ? r[COL.priority] : scoreOf(r, 'access', opts.profile);
      const fill = none ? NO_DATA_FILL : opts.style === 'priority' ? priorityColor(value) : BAND_FILL[bandOf(value, grid.grid, 'good')];
      const rect = L.rectangle(cellBounds(grid.grid, r[COL.row], r[COL.col]), {
        renderer: this.renderer, stroke: false, fillColor: fill, interactive: false,
        fillOpacity: none ? 0.4 : opts.style === 'priority' ? Math.min(0.75, 0.25 + value / 160) : (opts.opacity ?? 0.62)
      });
      rect.addTo(this.group);
      this.rects.set(`${r[COL.row]}-${r[COL.col]}`, rect);
    }
  }
}

const TYPE_ICON = {
  LightOut: 'lamp', UnsafeAtNight: 'moon', PathHazard: 'alert', WaterNotWorking: 'water', NoShade: 'sun', HeatSpot: 'thermo',
  FloodedStreet: 'wave', BlockedDrain: 'wave', RisingWater: 'wave', SmokeOrBurning: 'wind', StrongFumes: 'wind', DustCloud: 'wind'
};
const SEV_COLOR = { Info: '#2a78d6', Warning: '#eda100', Critical: '#d03b3b' };

export function mount(host) {
  const cleanups = [];
  let metric = 'score';          // 'priority' | 'score'
  let showReports = true, showAlerts = true, showPlaces = false;
  let features = [];
  let pickForAlert = false;
  let gridRes = null;
  let selectedCell = null;   // the square the planner chose, kept when the layer changes

  const info = mapInfo();
  const mapEl = h('div', { class: 'map', role: 'application', 'aria-label': t('pm.mapLabel') });
  const note = h('div', { class: 'map-note' });
  const legend = h('div', { class: 'legend open' });
  const ctl = h('div', { class: 'card map-ctl', style: { position: 'absolute', left: '.6rem', top: '.6rem', zIndex: 800, padding: '.6rem', maxWidth: 'min(92vw, 320px)' } });
  const wrap = h('div', { class: 'map-wrap' }, mapEl, note, ctl, legend);
  host.append(h('div', { class: 'pl-page fill' }, wrap));

  const map = createMap(mapEl, { center: KRAKOW, zoom: 12 });
  cleanups.push(watchResize(map, wrap), keyboardPick(map, t('a11y.mapHint')), () => map.remove());
  const overlay = L.layerGroup().addTo(map);
  const places = new PlacesLayer(map, { getFeatures: () => features, getKeys: () => RELIEF[layerMode(P.event)] || [] });
  places.setEnabled(false);
  cleanups.push(() => places.destroy());
  cachedGet('features', getFeatures).then((r) => { features = r.data; places.draw(); }).catch(() => {});
  const grid = new PlannerGrid(map, {
    onSelect: (latlng, c, row) => {
      if (pickForAlert) { pickForAlert = false; paintCtl(); openAlertDialog({ point: [latlng.lat, latlng.lng] }); return; }
      if (row) {
        grid.select(c.row, c.col);
        selectedCell = { row: c.row, col: c.col };
        info.show({ lat: latlng.lat, lon: latlng.lng, radiusText: t('cov.square', { r: '250 m' }) });
        openCellDrawer(cellId(c.row, c.col), { meta: gridRes?.data.grid });
      }
    }
  });
  cleanups.push(() => grid.destroy(), () => closeDrawer());

  function paintCtl() {
    clear(ctl);
    ctl.append(
      h('div', { class: 'stack tight' },
        h('div', { class: 'seg', role: 'group', 'aria-label': t('pm.metric') },
          [['priority', t('pm.priority')], ['score', t('pm.score')]].map(([m, l]) => h('button', { type: 'button', 'aria-pressed': String(metric === m), onclick: () => { metric = m; paintCtl(); draw(); } }, l))),
        h('label', { class: 'row small' }, h('input', { type: 'checkbox', checked: showReports ? true : null, onchange: (e) => { showReports = e.target.checked; drawOverlay(); } }), t('pm.reports')),
        h('label', { class: 'row small' }, h('input', { type: 'checkbox', checked: showAlerts ? true : null, onchange: (e) => { showAlerts = e.target.checked; drawOverlay(); } }), t('pm.alerts')),
        P.event === 'access' ? null : h('label', { class: 'row small' }, h('input', { type: 'checkbox', checked: showPlaces ? true : null, onchange: (e) => { showPlaces = e.target.checked; places.setEnabled(showPlaces); } }), `${t('map.places')} (${t('map.placesZoom')})`),
        h('button', { class: `btn sm ${pickForAlert ? 'primary' : ''}`, type: 'button', onclick: () => { pickForAlert = !pickForAlert; paintCtl(); if (pickForAlert) toast(t('pm.pickHint')); } }, icon('bell', 'sm'), pickForAlert ? t('pm.tapMap') : t('pm.newAlert')),
        info.el));
  }

  function paintLegend() {
    clear(legend);
    if (metric === 'priority') {
      legend.append(h('div', { class: 'small', style: { fontWeight: 700 } }, t('pm.priorityLegend', { event: t(`event.${P.event}`) })),
        h('div', { class: 'items' }, h('span', null, t('pm.lower')), PRIORITY_RAMP.map((c) => h('i', { style: { background: c, width: '1.1rem' } })), h('span', null, t('pm.higher'))),
        P.event === 'access' ? h('ul', { class: 'legend-rows' }, noDataRow2()) : '');
    } else if (P.event === 'access') {
      legend.append(accessLegend(), h('button', { class: 'btn sm quiet', type: 'button', style: { marginTop: '.3rem' }, onclick: () => openMethod(gridRes?.data.grid) }, icon('list', 'sm'), t('explain.fullMethod')));
    } else {
      const mode = layerMode(P.event);
      legend.append(...legendBody(mode, gridRes?.data.grid, { compact: true, onExplain: (layer) => openScoreExplainer({ layer, meta: gridRes?.data.grid }) }),
        h('button', { class: 'btn sm quiet', type: 'button', style: { marginTop: '.3rem' }, onclick: () => openMethod(gridRes?.data.grid) }, icon('list', 'sm'), t('explain.fullMethod')));
    }
  }

  /** The "No data" row of the legend: a hatched neutral swatch and the words, so it is not told apart by colour alone. */
  function noDataRow2() {
    return h('li', { class: 'nodata' }, noDataSwatch(), h('span', null, h('b', null, t('accl.noData')), ' · ', t('legend.noData.desc')));
  }

  /** Legend of the accessibility layer: the four bands of the chosen profile, then the grey "No data" row. */
  function accessLegend() {
    const meta = gridRes?.data.grid;
    const rows = ['Good', 'Fair', 'Weak', 'Critical'].map((b) => {
      const [from, to] = bandRange(b, meta, 'good');
      return h('li', { 'data-band': b }, h('i', { style: { background: BAND_FILL[b] } }), h('span', { class: 'num band-chip' }, `${Math.round(from)}–${Math.round(to)}`), h('span', null, h('b', null, bandText(b, 'good'))));
    });
    return h('div', { class: 'legend-block' },
      h('div', { class: 'row between' }, h('b', { class: 'small' }, `${t('legend.accessTitle')} · ${t(`accl.p.${P.profile}`)}`), infoButton(() => openAccessScoreExplainer({ profile: P.profile, meta }), t('explain.how'))),
      h('div', { class: 'tiny muted' }, t('explain.dir.access')),
      h('ul', { class: 'legend-rows' }, rows, noDataRow2()));
  }

  function draw() {
    if (!gridRes) return;
    const mode = layerMode(P.event);
    grid.draw(gridRes.data, { mode, profile: P.profile, style: metric === 'priority' ? 'priority' : 'score' });
    if (selectedCell) grid.select(selectedCell.row, selectedCell.col);   // the chosen square stays outlined when the layer changes
    paintLegend();
  }

  let reports = [], alerts = [];
  function drawOverlay() {
    overlay.clearLayers();
    if (showReports) {
      // Only the reports of the planning event: Heat shows heat reports, Night safety shows night reports, and so on.
      const wanted = apiLayerOf(P.event);
      for (const r of reports.filter((x) => x.status === 'Open' && x.layer === wanted)) {
        const m = iconMarker([r.latitude, r.longitude], TYPE_ICON[r.type] || 'flag', reportLayerOf(r.type), { small: true, title: t(`rtype.${r.type}`) });
        // Leaflet treats a string tooltip as HTML: pass an element so API text is only ever text.
        m.bindTooltip(h('span', null, `${t(`rtype.${r.type}`)} · ${t('report.supporters', { n: r.supporters })}`));
        m.on('click', () => openCellDrawer(r.cellId, { meta: gridRes?.data.grid }));
        m.addTo(overlay);
      }
    }
    if (showAlerts) {
      for (const a of alerts.filter((x) => x.status === 'Active' && new Date(x.expiresAt) > new Date())) {
        L.circle([a.latitude, a.longitude], { radius: a.radiusMeters, color: SEV_COLOR[a.severity], weight: 2, dashArray: '6 4', fillOpacity: 0.08, interactive: true })
          .bindTooltip(h('span', null, `${a.title} · ${a.devicesInArea ?? 0} ${t('al.phones')}`)).addTo(overlay);
      }
    }
  }

  async function load() {
    clear(note);
    try {
      gridRes = await loadGridFor(P.event);
      if (gridRes.stale) note.append(staleBanner(gridRes.savedAt));
      // Accessibility: the squares with no data are grey; say how many, up front.
      const a = P.event === 'access' ? gridRes.data.access : null;
      if (a) note.append(h('button', { class: 'banner nodata-banner small', type: 'button', style: { cursor: 'pointer', textAlign: 'left' }, onclick: () => openNoDataExplainer(a) }, icon('info', 'sm'), h('span', null, a.hasData ? t('pa.map.nodata', { n: a.cellsWithoutData.toLocaleString() }) : t('pa.nodata.none'))));
      draw();
      if (info.el.hidden) info.showText(t('pm.wholeCity'), t('cov.squares', { r: '250 m' }));
    } catch (e) {
      note.append(h('div', { class: 'banner danger small' }, icon('alert', 'sm'), h('span', null, t('err.generic'))));
    }
    try { reports = (await loadReports()).data; } catch { reports = []; }
    try { alerts = (await loadAlerts()).data; } catch { alerts = []; }
    drawOverlay();
    focus();
  }

  function focus() {
    const f = P.focus;
    if (!f) return;
    P.focus = null;
    map.setView([f.lat, f.lon], 16);
    info.show({ lat: f.lat, lon: f.lon, radiusText: t(f.cellId ? 'cov.square' : 'cov.alertArea', { r: '250 m' }) });
    if (f.cellId) { const [r, c] = f.cellId.split('-').map(Number); grid.select(r, c); selectedCell = { row: r, col: c }; openCellDrawer(f.cellId, { meta: gridRes?.data.grid }); }
  }

  // Changing the layer keeps the chosen square: its drawer is opened again for the new layer.
  // The profile is part of the accessibility layer: the same square stays chosen, repainted and explained for the new profile.
  const reload = async () => { paintCtl(); await load(); if (selectedCell && P.root?.querySelector('.drawer')) openCellDrawer(cellId(selectedCell.row, selectedCell.col), { meta: gridRes?.data.grid }); };
  cleanups.push(pOn('event', reload), pOn('profile', () => { if (P.event === 'access') reload(); }), pOn('data', load));
  paintCtl();
  paintLegend();
  load();
  return () => cleanups.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });
}
