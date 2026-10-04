// Planner map: the whole city coloured by priority (where to act first) or by score, with resident reports and
// active alert areas on top. Click any square to open its drill-down drawer.

import { h, icon, clear, toast } from './util.js';
import { t } from './i18n.js';
import { P, pOn, loadGridFor, loadReports, loadAlerts, openCellDrawer, openAlertDialog, staleBanner, closeDrawer } from './planner-common.js';
import { createMap, GridLayer, PlacesLayer, iconMarker, watchResize, mapInfo, KRAKOW } from './map.js';
import { legendBody, loadMethod, openMethod, openScoreExplainer } from './explain.js';
import { cachedGet, getFeatures } from './api.js';
import { BAND_FILL, PRIORITY_RAMP, COL, RELIEF, cellId, LAYERS, modeOfEvent, REPORT_LAYER } from './model.js';

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
  cleanups.push(watchResize(map, wrap), () => map.remove());
  const overlay = L.layerGroup().addTo(map);
  const places = new PlacesLayer(map, { getFeatures: () => features, getKeys: () => RELIEF[modeOfEvent(P.event)] });
  places.setEnabled(false);
  cleanups.push(() => places.destroy());
  cachedGet('features', getFeatures).then((r) => { features = r.data; places.draw(); }).catch(() => {});
  const grid = new GridLayer(map, {
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
        h('label', { class: 'row small' }, h('input', { type: 'checkbox', checked: showPlaces ? true : null, onchange: (e) => { showPlaces = e.target.checked; places.setEnabled(showPlaces); } }), `${t('map.places')} (${t('map.placesZoom')})`),
        h('button', { class: `btn sm ${pickForAlert ? 'primary' : ''}`, type: 'button', onclick: () => { pickForAlert = !pickForAlert; paintCtl(); if (pickForAlert) toast(t('pm.pickHint')); } }, icon('bell', 'sm'), pickForAlert ? t('pm.tapMap') : t('pm.newAlert')),
        info.el));
  }

  function paintLegend() {
    clear(legend);
    if (metric === 'priority') {
      legend.append(h('div', { class: 'small', style: { fontWeight: 700 } }, t('pm.priorityLegend', { event: t(`event.${P.event}`) })),
        h('div', { class: 'items' }, h('span', null, t('pm.lower')), PRIORITY_RAMP.map((c) => h('i', { style: { background: c, width: '1.1rem' } })), h('span', null, t('pm.higher'))));
    } else {
      const mode = modeOfEvent(P.event);
      legend.append(...legendBody(mode, gridRes?.data.grid, { compact: true, onExplain: (layer) => openScoreExplainer({ layer, meta: gridRes?.data.grid }) }),
        h('button', { class: 'btn sm quiet', type: 'button', style: { marginTop: '.3rem' }, onclick: () => openMethod(gridRes?.data.grid) }, icon('list', 'sm'), t('explain.fullMethod')));
    }
  }

  function draw() {
    if (!gridRes) return;
    const mode = modeOfEvent(P.event);
    grid.draw(gridRes.data, { mode, style: metric === 'priority' ? 'priority' : 'score' });
    if (selectedCell) grid.select(selectedCell.row, selectedCell.col);   // the chosen square stays outlined when the layer changes
    paintLegend();
  }

  let reports = [], alerts = [];
  function drawOverlay() {
    overlay.clearLayers();
    if (showReports) {
      // Only the reports of the planning event: Heat shows heat reports, Night safety shows night reports, and so on.
      const wanted = LAYERS[modeOfEvent(P.event)].api;
      for (const r of reports.filter((x) => x.status === 'Open' && x.layer === wanted)) {
        const m = iconMarker([r.latitude, r.longitude], TYPE_ICON[r.type] || 'flag', REPORT_LAYER[r.type] || 'safety', { small: true, title: t(`rtype.${r.type}`) });
        m.bindTooltip(`${t(`rtype.${r.type}`)} · ${t('report.supporters', { n: r.supporters })}`);
        m.on('click', () => openCellDrawer(r.cellId, { meta: gridRes?.data.grid }));
        m.addTo(overlay);
      }
    }
    if (showAlerts) {
      for (const a of alerts.filter((x) => x.status === 'Active' && new Date(x.expiresAt) > new Date())) {
        L.circle([a.latitude, a.longitude], { radius: a.radiusMeters, color: SEV_COLOR[a.severity], weight: 2, dashArray: '6 4', fillOpacity: 0.08, interactive: true })
          .bindTooltip(`${a.title} · ${a.devicesInArea ?? 0} ${t('al.phones')}`).addTo(overlay);
      }
    }
  }

  async function load() {
    clear(note);
    try {
      gridRes = await loadGridFor(P.event);
      if (gridRes.stale) note.append(staleBanner(gridRes.savedAt));
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
  cleanups.push(pOn('event', async () => { await load(); if (selectedCell && P.root?.querySelector('.drawer')) openCellDrawer(cellId(selectedCell.row, selectedCell.col), { meta: gridRes?.data.grid }); }), pOn('data', load));
  paintCtl();
  paintLegend();
  load();
  return () => cleanups.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });
}
