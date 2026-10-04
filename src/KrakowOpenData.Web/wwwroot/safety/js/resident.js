// Resident view: a full-screen map coloured by the chosen view (night safety, heat or both), a place card with the
// reasons behind the score, and tools to report a concern and check a walk. Each view shows only its own data:
// Night safety = night-safety score, factors, help and reports; Heat = heat score, factors, help and reports.
// Phones get a bottom sheet; wide screens get a side panel. Works offline from saved data.

import { h, icon, clear, toast, openDialog, timeAgo, formatDistance, clamp, directionsLink, announce, keepFocus } from './util.js';
import { state, set, on, eventForMode, isOffline } from './state.js';
import { t, getLang } from './i18n.js';
import { cachedGet, peek, getConditions, getGrid, getPlace, getFeatures, getReportTypes, confirmReport, errorText, ApiError, DOCS_URL } from './api.js';
import { createMap, GridLayer, PlacesLayer, iconMarker, pinMarker, watchResize, mapInfo, coverageCircle, KRAKOW, keyboardPick } from './map.js';
import { bandOf, kindOf, BAND_FILL, scoreOf, indexGrid, cellOf, FACTOR_ICON, FACTOR_LAYER, RELIEF, nearestFromFeatures, COL, LAYERS, MODE_KEYS, asMode } from './model.js';
import { bandText, infoButton, legendBody, loadMethod, openFactorExplainer, openMethod, openScoreExplainer, scoreSummary } from './explain.js';
import { topbar, statusPill, openHowItWorks, flushOutbox, notificationToggle, displayToggles, readAloudButton, stopReading } from './chrome.js';
import { renderReportView } from './report.js';
import { renderWalkView } from './walk.js';
import { startAlerts } from './alerts.js';
import { searchBox } from './search.js';
import { addressLine, coords } from './geo.js';
import { telegramCard } from './telegram.js';
import { renderAccessView } from './access.js';
import { wheelchairBadge } from './wheelchair.js';

/** Which score layers a view shows: exactly one. Night safety, heat, flood and air never mix. */
export const layersOf = (mode) => [asMode(mode)];

export function mountResident(root) {
  const cleanups = [];
  const ctx = {
    root, map: null, gridLayer: null, grid: null, gridSavedAt: null, gridStale: false, features: null, conditions: null,
    reportTypes: null, selected: null, view: 'home', cleanups
  };

  // ── Scaffold ───────────────────────────────────────────────────────────────
  const pill = statusPill();
  cleanups.push(() => pill.destroy());
  const bar = topbar({ pill });

  const modeSeg = h('div', { class: 'seg', role: 'group', 'aria-label': t('mode.label') });
  const condStrip = h('div', { class: 'cond-strip', role: 'region', 'aria-label': t('cond.title') });
  const resBar = h('div', { class: 'res-bar' }, h('span', { class: 'small muted hide-sm' }, t('mode.label')), modeSeg,
    h('button', { class: 'btn sm quiet', type: 'button', onclick: openHowItWorks }, icon('info', 'sm'), h('span', null, t('how.link'))));

  const mapEl = h('div', { class: 'map', role: 'application', 'aria-label': t('map.label') });
  const info = mapInfo();
  ctx.info = info;
  // Not a live region: the stack is repainted on every poll. New alerts are announced once (js/alerts.js).
  const alertStack = h('div', { class: 'alert-stack' });
  const mapTop = h('div', { class: 'map-top' }, info.el, alertStack);
  const mapNote = h('div', { class: 'map-note' });
  const legend = h('div', { class: 'legend', id: 'map-legend' });
  const locateBtn = h('button', { class: 'btn icon', type: 'button', 'aria-label': t('map.locate'), title: t('map.locate'), onclick: () => locateMe() }, icon('locate'));
  const legendBtn = h('button', { class: 'btn icon', type: 'button', 'aria-label': t('map.legend'), title: t('map.legend'), 'aria-expanded': 'false', 'aria-controls': 'map-legend',
    onclick: () => legendBtn.setAttribute('aria-expanded', String(legend.classList.toggle('open'))) }, icon('layers'));
  let placesOn = true;
  const placesBtn = h('button', { class: 'btn icon', type: 'button', 'aria-pressed': 'true', 'aria-label': t('map.places'), title: `${t('map.places')} (${t('map.placesZoom')})`,
    onclick: () => { placesOn = !placesOn; placesBtn.setAttribute('aria-pressed', String(placesOn)); ctx.places.setEnabled(placesOn); } }, icon('building'));
  const mapBtns = h('div', { class: 'map-btns top' }, locateBtn, placesBtn, legendBtn);
  const mapWrap = h('div', { class: 'map-wrap' }, mapEl, mapTop, mapNote, mapBtns, legend);

  const panelTitle = h('h2', { id: 'panel-title', tabindex: '-1' });
  const panelBack = h('button', { class: 'btn sm', type: 'button', onclick: () => showView('home') }, icon('left', 'sm'), t('nav.backMenu'));
  const panelHead = h('div', { class: 'panel-head' }, panelBack, panelTitle);
  const panelBody = h('div', { class: 'panel-body', id: 'panel-body' });
  const handle = h('button', { class: 'handle', type: 'button', 'aria-label': t('sheet.toggle'), 'aria-controls': 'panel-body' });
  // Phones start with the map as the hero and a small sheet; picking a place opens the sheet.
  const panel = h('section', { class: 'panel', 'data-state': matchMedia('(max-width: 959.98px)').matches ? 'peek' : 'half', 'aria-labelledby': 'panel-title' }, handle, panelHead, panelBody);

  // The panel comes first in reading and Tab order (on wide screens it is also drawn on the left); the map follows.
  const main = h('main', { class: 'res-main', id: 'main', tabindex: '-1' }, h('h1', { class: 'sr-only' }, t('app.title')), panel, mapWrap);
  root.append(bar, resBar, condStrip, main);
  Object.assign(ctx, { panel, panelBody, panelTitle, mapWrap });

  // ── Map ────────────────────────────────────────────────────────────────────
  const map = createMap(mapEl, { center: state.me ? [state.me.lat, state.me.lon] : KRAKOW, zoom: state.me ? 15 : 13 });
  ctx.map = map;
  cleanups.push(watchResize(map, mapWrap));
  cleanups.push(keyboardPick(map, t('a11y.mapHint')));
  ctx.gridLayer = new GridLayer(map, {
    onSelect: (latlng) => {
      if (ctx.pick && ctx.pick(latlng)) return;      // the walk check is waiting for a point
      if (ctx.view === 'report') return;             // the report pin has its own click handling
      selectPoint(latlng.lat, latlng.lng);
    },
    onHover: (latlng, row, tip) => {
      if (!row || matchMedia('(pointer: coarse)').matches) { tip.remove(); return; }
      const m = mode();
      const score = scoreOf(row, m);
      const band = bandOf(score, ctx.grid?.grid, kindOf(m));
      tip.setLatLng(latlng).setContent(`${t(`mode.${m}.score`)}: ${score} · ${bandText(band, kindOf(m))} (${t(`explain.dir.${m}`)})`).addTo(map);
    }
  });
  // Places that feed the scores (water, parks, toilets, refuges, night-open places, defibrillators), shown from zoom 14.
  ctx.places = new PlacesLayer(map, {
    getFeatures: () => ctx.features,
    getKeys: () => RELIEF[mode()],
    onWalk: (f) => showView('walk', { to: [f.latitude, f.longitude], toLabel: f.name || t(`kind.${f.kind}`) })
  });
  cleanups.push(() => { ctx.places.destroy(); ctx.gridLayer.destroy(); map.remove(); });

  let selMarker = null;
  let meMarker = null;
  let coverage = null;

  function drawMe() {
    meMarker?.remove();
    meMarker = null;
    if (state.me) meMarker = iconMarker([state.me.lat, state.me.lon], null, 'me', { title: t('home.myArea') }).addTo(map);
  }
  cleanups.push(on('me', () => { drawMe(); if (ctx.view === 'home') softRender(); }));
  drawMe();

  // ── Bottom sheet ───────────────────────────────────────────────────────────
  const STATES = ['peek', 'half', 'full'];
  const setSheet = (s) => { panel.dataset.state = s; main.dataset.sheet = s; handle.setAttribute('aria-expanded', String(s !== 'peek')); };
  setSheet(panel.dataset.state);
  ctx.setSheet = setSheet;
  let drag = null;
  handle.addEventListener('pointerdown', (e) => {
    drag = { y: e.clientY, h: panel.offsetHeight, moved: false };
    panel.classList.add('dragging');
    handle.setPointerCapture(e.pointerId);
  });
  handle.addEventListener('pointermove', (e) => {
    if (!drag) return;
    if (Math.abs(e.clientY - drag.y) > 5) drag.moved = true;
    const max = main.offsetHeight - 8;
    main.style.setProperty('--sheet-h', `${clamp(drag.h + (drag.y - e.clientY), 90, max)}px`);
  });
  const endDrag = () => {
    if (!drag) return;
    panel.classList.remove('dragging');
    const hgt = panel.offsetHeight, max = main.offsetHeight - 8;
    main.style.removeProperty('--sheet-h');
    if (!drag.moved) {
      setSheet(STATES[(STATES.indexOf(panel.dataset.state) + 1) % 3]);
    } else {
      const targets = { peek: 120, half: max * 0.52, full: max };
      setSheet(Object.entries(targets).sort((a, b) => Math.abs(a[1] - hgt) - Math.abs(b[1] - hgt))[0][0]);
    }
    drag = null;
  };
  handle.addEventListener('pointerup', endDrag);
  handle.addEventListener('pointercancel', endDrag);
  handle.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setSheet(STATES[(STATES.indexOf(panel.dataset.state) + 1) % 3]); }
  });

  // ── Mode ───────────────────────────────────────────────────────────────────
  const mode = () => state.mode || 'safety';
  ctx.mode = mode;

  function paintModes() {
    // Built once; later only aria-pressed changes, so the pressed button keeps keyboard focus.
    if (modeSeg.childElementCount) {
      modeSeg.querySelectorAll('button[data-mode]').forEach((b) => b.setAttribute('aria-pressed', String(mode() === b.dataset.mode)));
      return;
    }
    for (const m of MODE_KEYS) {
      modeSeg.append(h('button', {
        type: 'button', 'data-mode': m, 'aria-pressed': String(mode() === m),
        onclick: () => { set({ mode: m }); announce(t(`mode.${m}`)); }
      }, icon(LAYERS[m].icon, 'sm'), t(`mode.${m}`)));
    }
  }
  paintModes();
  cleanups.push(on('mode', () => {
    paintModes();
    redrawGrid();
    ctx.places?.draw();
    paintLegend();
    paintConditions();
    ctx.alerts?.repaint();
    // The selected place is read again for the new view, so the card never shows data from the other one.
    if (ctx.selected && ctx.view === 'place') selectPoint(ctx.selected.lat, ctx.selected.lon, { keepView: true });
    else renderView();
  }));

  // ── Legend ─────────────────────────────────────────────────────────────────
  function paintLegend() {
    clear(legend);
    legend.append(
      ...legendBody(mode(), ctx.grid?.grid, { compact: true, onExplain: (layer) => openScoreExplainer({ layer, meta: ctx.grid?.grid }) }),
      h('div', { class: 'tiny muted', style: { marginTop: '.3rem' } }, t('legend.note')),
      h('button', { class: 'btn sm quiet', type: 'button', style: { marginTop: '.3rem' }, onclick: () => openMethod(ctx.grid?.grid) }, icon('list', 'sm'), t('explain.fullMethod')));
  }
  paintLegend();
  loadMethod().then(paintLegend).catch(() => {});

  // ── Data loading ───────────────────────────────────────────────────────────
  function redrawGrid() {
    if (!ctx.grid) return;
    ctx.gridLayer.draw(ctx.grid, { mode: mode() });
    ctx.gridIndex = indexGrid(ctx.grid);
    if (ctx.selected?.row !== undefined) ctx.gridLayer.select(ctx.selected.row, ctx.selected.col);
  }

  function showMapNote(content, kind = 'info') {
    clear(mapNote);
    if (!content) return;
    mapNote.append(h('div', { class: `banner ${kind} small` }, content));
  }

  function applyGrid(data, savedAt, stale) {
    ctx.grid = data;
    ctx.gridSavedAt = savedAt;
    ctx.gridStale = stale;
    redrawGrid();
    if (stale) showMapNote([icon('offline', 'sm'), h('span', null, t('map.savedData', { when: timeAgo(savedAt, t, getLang()) }))], 'warn');
    else showMapNote(null);
    pill.setSaved(stale ? savedAt : null);
    if (ctx.selected && ctx.view === 'place') softRender();
  }

  let retryTimer;
  async function loadGrid() {
    clearTimeout(retryTimer);
    try {
      const { data, savedAt, stale } = await cachedGet('grid:all', () => getGrid('both'));
      applyGrid(data, savedAt, stale);
    } catch (e) {
      if (e instanceof ApiError && e.status === 503) {
        showMapNote([icon('refresh', 'sm'), h('span', null, t('map.preparing'))]);
        retryTimer = setTimeout(loadGrid, (e.retryAfter || 20) * 1000);
      } else if (!ctx.grid) {
        showMapNote([icon('alert', 'sm'), h('div', { class: 'grow' }, errorText(e, t)),
          h('button', { class: 'btn sm', type: 'button', onclick: loadGrid }, t('common.retry'))], 'danger');
      }
    }
  }
  cleanups.push(() => clearTimeout(retryTimer));
  ctx.refreshGrid = loadGrid;

  async function loadConditions() {
    try {
      const { data, stale } = await cachedGet('conditions', getConditions);
      ctx.conditions = data;
      ctx.conditionsStale = stale;
      if (!state.mode) set({ mode: asMode(data.suggestedMode) });
      paintConditions();
      if (ctx.view === 'home') softRender();
    } catch { /* conditions are optional */ }
  }

  async function loadFeatures() {
    try { ctx.features = (await cachedGet('features', getFeatures)).data; ctx.places?.draw(); } catch { /* offline nearest is optional */ }
  }

  async function loadTypes() {
    try { ctx.reportTypes = (await cachedGet('report-types', getReportTypes)).data; } catch { /* the report view has a built-in list */ }
  }

  // ── Conditions strip (only what matters for the chosen view) ───────────────
  function paintConditions() {
    clear(condStrip);
    const c = ctx.conditions;
    if (!c) { condStrip.hidden = true; return; }
    condStrip.hidden = false;
    const m = mode();
    const chips = [];
    if (m === 'heat') {
      const hot = c.heat.level >= 1;
      chips.push(chip(hot ? 'warn' : 'heat', 'thermo', [c.heat.temperatureC !== null ? `${Math.round(c.heat.temperatureC)}°C` : '–', ' · ', t(`heat.${c.heat.pressure}`)]));
      const warnings = (c.warnings || []).filter((w) => w.level > 0);
      if (warnings.length) chips.push(chip('warn', 'alert', t('cond.warnings', { n: warnings.length })));
    }
    if (m === 'safety') {
      chips.push(chip(c.isDark ? 'safety' : '', c.isDark ? 'moon' : 'sun',
        c.isDark ? t('cond.dark', { time: c.sunriseLocal || '' }) : t('cond.light', { time: c.sunsetLocal || '' })));
    }
    if (m === 'flood') {
      chips.push(c.hydro.elevatedGauges > 0
        ? chip('danger', 'wave', t('cond.rivers', { n: c.hydro.elevatedGauges }))
        : chip('ok', 'wave', t('cond.riversOk')));
    }
    if (m === 'air') {
      if (c.air.band && c.air.band !== 'Unknown') {
        chips.push(chip(c.air.band === 'Poor' || c.air.band === 'VeryPoor' ? 'danger' : c.air.band === 'Good' ? 'ok' : '', 'wind', t('cond.air', { band: t(`air.${c.air.band}`) })));
      } else {
        chips.push(chip('', 'wind', t('air.Unknown')));
      }
      if (c.air.pm25 !== null && c.air.pm25 !== undefined) chips.push(chip('', 'info', `PM2.5 ${Math.round(c.air.pm25)} µg/m³`));
    }
    if (ctx.conditionsStale) chips.push(chip('warn', 'offline', t('cond.saved')));
    condStrip.append(...chips);
  }

  function chip(kind, ic, content) {
    return h('button', { class: `chip ${kind}`, type: 'button', onclick: openConditions, style: { border: 0, cursor: 'pointer', minHeight: '32px' } },
      icon(ic, 'sm'), h('span', null, content));
  }

  function openConditions() {
    const c = ctx.conditions;
    if (!c) return;
    const m = mode();
    const rows = [];
    if (m === 'heat') {
      rows.push(h('dt', null, t('cond.heat')), h('dd', null, `${t(`heat.${c.heat.pressure}`)}${c.heat.temperatureC !== null ? ` (${c.heat.temperatureC.toFixed(1)} °C)` : ''}`));
    }
    if (m === 'air') {
      rows.push(h('dt', null, t('cond.airTitle')), h('dd', null, c.air.pm25 !== null ? `${t(`air.${c.air.band}`)} · PM2.5 ${c.air.pm25} µg/m³ (${t('cond.worstStation')}${c.air.pm25Average != null ? `; ${t('cond.average')} ${c.air.pm25Average}` : ''})` : t('air.Unknown')));
    }
    if (m === 'safety') rows.push(h('dt', null, t('cond.daylight')), h('dd', null, `${c.sunriseLocal ?? '–'} → ${c.sunsetLocal ?? '–'} ${c.isDark ? '· ' + t('cond.nowDark') : ''}`));
    if (m === 'flood') rows.push(h('dt', null, t('cond.riversTitle')), h('dd', null, c.hydro.elevatedGauges ? t('cond.rivers', { n: c.hydro.elevatedGauges }) : t('cond.riversOk')));
    const warnings = m === 'heat' ? (c.warnings || []).filter((w) => w.level > 0) : [];
    openDialog((close) => ({
      title: t('cond.title'),
      body: h('div', { class: 'stack' },
        h('dl', { class: 'kv' }, rows),
        warnings.length
          ? h('div', null, h('p', { class: 'sect-title' }, t('cond.warningsTitle')),
            h('ul', { class: 'list' }, warnings.map((w) => h('li', null, h('b', null, `${w.eventName} · ${t('cond.level', { n: w.level })}`), h('p', { class: 'small muted' }, (w.content || '').slice(0, 220))))))
          : (m === 'heat' ? h('p', { class: 'small muted' }, t('cond.noWarnings')) : null),
        h('p', { class: 'tiny muted' }, t('cond.source'))),
      footer: h('button', { class: 'btn primary', type: 'button', onclick: () => close('ok') }, t('common.done'))
    }));
  }

  // ── Selecting a place ──────────────────────────────────────────────────────
  let selToken = 0;

  /** Pans the map so a point sits in the middle of the part of the map the bottom sheet does not cover. */
  function revealPoint(lat, lon) {
    const size = map.getSize();
    const visible = size.y - panel.offsetHeight;
    if (visible < 120) return;
    const pt = map.latLngToContainerPoint([lat, lon]);
    const target = visible / 2 + 40;
    if (pt.y > visible - 30 || pt.y < 40) map.panBy([0, pt.y - target], { animate: true });
  }

  const searchRadius = () => ctx.grid?.grid?.searchRadiusMeters || 1500;

  async function selectPoint(lat, lon, { fly = false, keepView = false, label = null } = {}) {
    const token = ++selToken;
    const meta = ctx.grid?.grid;
    const c = meta ? cellOf(meta, lat, lon) : null;
    ctx.selected = { lat, lon, row: c?.row, col: c?.col, place: null, loading: true, error: null, label };
    if (c) ctx.gridLayer.select(c.row, c.col);
    selMarker?.remove();
    selMarker = pinMarker([lat, lon]).addTo(map);
    coverage?.remove();
    coverage = coverageCircle([lat, lon], searchRadius(), t('cov.circle', { r: formatDistance(searchRadius()) })).addTo(map);
    info.show({ lat, lon, fallback: label, radiusMeters: searchRadius() });
    // Show the whole circle that is searched, so the radius covered is visible.
    if (fly) map.flyToBounds(coverage.getBounds(), { maxZoom: 15, duration: 0.6 });
    if (!keepView) {
      ctx.view = 'place';
      setSheet('half');
      // On phones the sheet covers the lower half of the map: pan so the selected spot stays visible above it.
      if (matchMedia('(max-width: 959.98px)').matches) setTimeout(() => revealPoint(lat, lon), 280);
    }
    renderView();

    const event = eventForMode(mode());
    const key = `place:${c ? `${c.row}-${c.col}` : `${lat.toFixed(3)},${lon.toFixed(3)}`}:${event}`;
    try {
      const { data, stale, savedAt } = await cachedGet(key, () => getPlace(lat, lon, event));
      if (token !== selToken) return;
      Object.assign(ctx.selected, { place: data, stale, savedAt, loading: false });
    } catch (e) {
      if (token !== selToken) return;
      Object.assign(ctx.selected, { loading: false, error: e });
    }
    renderView();
    const m = asMode(mode()), layer = ctx.selected.place?.[m];
    if (layer && ctx.view === 'place') announce(t('a11y.placeScore', { label: t(`mode.${m}.score`), n: Math.round(layer.score), band: bandText(layer.band, kindOf(m)), dir: t(`explain.dir.${m}`) }));
  }
  ctx.selectPoint = selectPoint;

  function clearSelection() {
    selToken++;
    ctx.selected = null;
    selMarker?.remove();
    selMarker = null;
    coverage?.remove();
    coverage = null;
    info.hide();
    ctx.gridLayer.select(null);
  }
  ctx.clearSelection = clearSelection;

  // ── Views in the panel ─────────────────────────────────────────────────────
  function showView(name, props) {
    stopReading();
    ctx.view = name;
    ctx.viewProps = props;
    if (name === 'home') clearSelection();
    if (name !== 'walk') ctx.walkCleanup?.();
    if (name !== 'report') ctx.reportCleanup?.();
    if (name !== 'access') ctx.accessCleanup?.();
    if (name !== 'place' && name !== 'home') { coverage?.remove(); coverage = null; }
    renderView(true);
  }
  ctx.showView = showView;

  function renderView(focus = false) {
    const body = panelBody;
    const scroll = body.scrollTop;
    // A rebuild keeps keyboard focus on the same control (data-fk keys); if that control is gone, focus goes to the title.
    keepFocus(body, () => {
      clear(body);
      panelBack.hidden = ctx.view === 'home';
      if (ctx.view === 'home') { panelTitle.textContent = t('home.title'); renderHome(body); }
      else if (ctx.view === 'place') { panelTitle.textContent = t('place.title'); renderPlace(body); }
      else if (ctx.view === 'report') { panelTitle.textContent = t('report.title'); renderReportView(ctx, body); }
      else if (ctx.view === 'walk') { panelTitle.textContent = t(`walk.title.${mode()}`); renderWalkView(ctx, body); }
      else if (ctx.view === 'access') { panelTitle.textContent = t('acc.title'); renderAccessView(ctx, body); }
    }, panelTitle);
    if (focus) panelTitle.focus({ preventScroll: true });
    else body.scrollTop = scroll;
  }
  ctx.render = () => renderView();

  /** For updates that arrive on their own (conditions, alerts): never rebuild the menu while the user is typing in it. */
  function softRender() {
    const el = document.activeElement;
    if (el && panelBody.contains(el) && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA')) return;
    renderView();
  }

  // Home (the menu) ------------------------------------------------------------
  function renderHome(body) {
    const c = ctx.conditions;
    const stack = h('div', { class: 'stack' });
    if (c && MODE_KEYS.includes(c.suggestedMode) && mode() !== c.suggestedMode) {
      stack.append(h('div', { class: 'banner info' }, icon('info'), h('div', { class: 'grow' }, t(`home.suggest.${c.suggestedMode}`),
        h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => set({ mode: c.suggestedMode }) }, t('home.switch', { mode: t(`mode.${c.suggestedMode}`) }))))));
    }

    // 1. Find a place
    const box = searchBox({
      label: t('home.searchLabel'), placeholder: t('home.searchPlaceholder'), near: () => [map.getCenter().lat, map.getCenter().lng],
      onPick: (r) => selectPoint(r.lat, r.lon, { fly: true, label: r.label })
    });
    box.querySelector('input').dataset.fk = 'home-q';
    box.querySelector('input').addEventListener('focus', () => { if (panel.dataset.state === 'peek') setSheet('half'); });
    stack.append(box);

    // 2. Do something
    stack.append(h('div', { class: 'row wrap' },
      h('button', { class: 'btn primary', type: 'button', 'data-fk': 'home-locate', onclick: () => locateMe() }, icon('locate'), t('home.useLocation')),
      h('button', { class: 'btn', type: 'button', 'data-fk': 'home-walk', onclick: () => showView('walk') }, icon('walk'), t(`home.walk.${mode()}`))),
      h('p', { class: 'small muted' }, t(`home.walkHelp.${mode()}`)),
      h('p', { class: 'small muted' }, t('home.tapHint')));

    // 2b. Accessibility (Kraków bez barier): barriers and amenities for a wheelchair, a pram or limited mobility
    stack.append(h('div', { class: 'card flat stack tight' },
      h('button', { class: 'btn', type: 'button', onclick: () => showView('access') }, h('span', { 'aria-hidden': 'true' }, '♿'), t('acc.open')),
      h('p', { class: 'small muted' }, t('acc.homeHelp'))));

    // 3. Alerts
    stack.append(h('div', { class: 'card flat' }, h('h3', null, t('home.alertsTitle')),
      state.me
        ? h('div', { class: 'stack tight' },
          h('p', { class: 'small muted' }, icon('bell', 'sm'), ' ', t('home.alertsOn'), ctx.alerts?.lastChecked ? ` · ${t('alerts.checked', { when: timeAgo(ctx.alerts.lastChecked, t, getLang()) })}` : ''),
          addressLine(state.me.lat, state.me.lon),
          h('div', { class: 'row wrap' }, notificationToggle(),
            h('button', { class: 'btn sm quiet', type: 'button', 'data-fk': 'home-clear-area', onclick: () => { set({ me: null }); ctx.alerts?.refresh(); } }, t('home.clearArea'))))
        : h('div', { class: 'stack tight' }, h('p', { class: 'small muted' }, t('home.alertsHelp')),
          h('div', { class: 'row wrap' }, h('button', { class: 'btn sm', type: 'button', 'data-fk': 'home-alerts-set', onclick: () => locateMe(true) }, icon('bell', 'sm'), t('home.alertsSet')),
            notificationToggle()))));

    stack.append(telegramCard()); // "Powiadomienia w Telegramie" (telegram.js); hidden when the server has no bot

    // 4. How to read the colours
    stack.append(h('div', { class: 'card flat' }, h('h3', null, t('home.read')),
      ...legendBody(mode(), ctx.grid?.grid, { onExplain: (layer) => openScoreExplainer({ layer, meta: ctx.grid?.grid }) }),
      h('button', { class: 'btn sm quiet', type: 'button', 'data-fk': 'home-method', style: { marginTop: '.4rem' }, onclick: () => openMethod(ctx.grid?.grid) }, icon('list', 'sm'), t('explain.fullMethod'))));

    // 5. Display: high contrast and larger text
    stack.append(h('div', { class: 'card flat' }, h('h3', null, t('display.title')), displayToggles()));

    stack.append(h('div', { class: 'row wrap small' },
      h('button', { class: 'btn sm quiet', type: 'button', 'data-fk': 'home-how', onclick: openHowItWorks }, icon('info', 'sm'), t('how.link')),
      h('a', { class: 'btn sm quiet', href: '#/planner' }, icon('dash', 'sm'), t('about.planner')),
      h('a', { class: 'btn sm quiet', href: '#/accessibility' }, icon('a11y', 'sm'), t('stmt.link')),
      h('a', { class: 'btn sm quiet', href: DOCS_URL, target: '_blank', rel: 'noopener' }, icon('external', 'sm'), t('about.api'))),
      h('p', { class: 'tiny muted' }, t('how.notCrime')));
    body.append(stack);
  }

  // Place card -----------------------------------------------------------------
  function renderPlace(body) {
    const sel = ctx.selected;
    if (!sel) { showView('home'); return; }
    const m = mode();
    const layers = layersOf(m);
    const place = sel.place;
    const gridRow = sel.row !== undefined ? ctx.gridIndex?.get(`${sel.row}-${sel.col}`) : null;
    const approx = !place && gridRow && !sel.loading;
    const stack = h('div', { class: 'stack' });

    // Where this is, and how far the lookup reaches
    stack.append(h('div', { class: 'stack tight' },
      addressLine(sel.lat, sel.lon, { fallback: sel.label || (place?.label ? t('cell.near', { name: place.label }) : null) }),
      h('div', { class: 'small muted' }, icon('target', 'sm'), ' ', coverageText(m)),
      sel.stale && place ? h('span', { class: 'chip warn' }, icon('offline', 'sm'), t('place.saved', { when: timeAgo(sel.savedAt, t, getLang()) })) : null));

    if (sel.loading && !gridRow) {
      stack.append(h('div', { class: 'skeleton', style: { height: '84px' } }), h('div', { class: 'skeleton', style: { height: '160px' } }));
      body.append(stack);
      return;
    }

    const layerData = (k) => place ? place[k] : gridRow ? { score: gridRow[COL[k]], band: bandOf(gridRow[COL[k]], ctx.grid?.grid, kindOf(k)), factors: null, reportPenalty: 0, baseScore: gridRow[COL[k]] } : null;
    const data = Object.fromEntries(layers.map((k) => [k, layerData(k)]));

    if (layers.some((k) => !data[k])) {
      // No score for this spot: offline and not in the saved map, or outside the mapped area.
      if (sel.error) {
        const offlineNoScore = isOffline() && ctx.features;
        stack.append(h('div', { class: offlineNoScore ? 'banner info' : 'banner danger' }, icon(offlineNoScore ? 'offline' : 'alert'), h('div', { class: 'grow' }, offlineNoScore ? t('place.noSaved') : errorText(sel.error, t),
          h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => selectPoint(sel.lat, sel.lon, { keepView: true, label: sel.label }) }, t('common.retry'))))));
        stack.append(placeActions(sel));
        if (offlineNoScore) stack.append(nearbyBlock(sel, null));
      } else {
        stack.append(h('div', { class: 'banner info' }, icon('info'), h('span', null, t('place.outside'))), placeActions(sel));
      }
      body.append(stack);
      return;
    }

    // The score of the chosen view
    stack.append(h('div', { class: 'place-scores' }, layers.map((k) => scoreBox(k, data[k], true, place))));
    const reader = readAloudButton(() => placeSpeech(body, sel, place, m, data[m]), 'place');
    if (reader) stack.append(h('div', { class: 'row wrap' }, reader));

    if (approx) stack.append(h('div', { class: 'banner info small' }, icon('offline', 'sm'), h('span', null, t('place.approx'))));
    if (sel.loading) stack.append(h('div', { class: 'skeleton', style: { height: '120px' } }));
    if (place && layers.some((k) => place[k].reportPenalty > 0)) stack.append(h('div', { class: 'banner small' }, icon('flag', 'sm'), h('span', null, t('place.reportsAffect'))));

    stack.append(placeActions(sel));
    stack.append(nearbyBlock(sel, place));
    if (m === 'heat') stack.append(waterNote(place));
    if (m === 'flood' || m === 'air') stack.append(dataGapNote(place, m));

    // Why this score: only the factors of the chosen view
    if (place) {
      stack.append(h('details', { class: 'card flat', open: true }, h('summary', { style: { cursor: 'pointer', fontWeight: 700 } }, t('place.why')),
        h('div', { class: 'stack', style: { marginTop: '.6rem' } },
          layers.map((k) => h('div', null,
            h('p', { class: 'sect-title' }, `${t(`mode.${k}.score`)} · ${Math.round(place[k].baseScore)}${place[k].reportPenalty ? ` − ${place[k].reportPenalty} ${t(k === 'safety' || k === 'heat' ? 'place.fromReports' : 'place.fromReportsLive')}` : ''} = ${Math.round(place[k].score)}`),
            place[k].factors.map((f) => factorRow(f, k)))),
          h('div', { class: 'row wrap' }, h('button', { class: 'btn sm', type: 'button', onclick: () => openScoreExplainer({ layer: m, place, meta: ctx.grid?.grid }) }, icon('info', 'sm'), t('explain.how')),
            h('button', { class: 'btn sm quiet', type: 'button', onclick: () => openMethod(ctx.grid?.grid) }, icon('list', 'sm'), t('explain.fullMethod'))),
          h('p', { class: 'tiny muted' }, t('place.whyNote')))));
    }

    // Reports of the chosen view
    const wanted = new Set(layers.map((k) => LAYERS[k].api));
    const reports = (place?.reports || []).filter((r) => wanted.has(r.layer));
    if (reports.length) stack.append(h('section', null, h('h3', null, t('place.reportsTitle')), h('ul', { class: 'list' }, reports.slice(0, 6).map(reportRow))));
    stack.append(h('p', { class: 'tiny muted' }, t('how.notCrime')));
    body.append(stack);
  }

  /** The place card as one text for "Read aloud": where, the score in words, what it is made of, and help nearby. */
  function placeSpeech(body, sel, place, m, layer) {
    const where = body.querySelector('.addr')?.textContent.trim() || sel.label || coords(sel.lat, sel.lon);
    const parts = [where, t('a11y.placeScore', { label: t(`mode.${m}.score`), n: Math.round(layer.score), band: bandText(layer.band, kindOf(m)), dir: t(`explain.dir.${m}`) })];
    if (place?.[m]?.factors?.length) parts.push(`${t('read.factors')}: ${place[m].factors.map((f) => `${t(`factor.${f.key}`)} ${factorValue(f)}`).join('; ')}`);
    const keys = RELIEF[m];
    const near = place ? place.nearest.filter((n) => keys.includes(n.key)).slice(0, 3) : [];
    if (near.length) parts.push(`${t('read.near')}: ${near.map((n) => `${n.name || t(`kind.${n.kind}`)}, ${formatDistance(n.distanceMeters)}`).join('; ')}`);
    return parts.join('. ');
  }

  /** What the numbers on the card cover (matches the dashed circle on the map). */
  function coverageText(m) {
    const r = formatDistance(searchRadius());
    return t(`cov.${asMode(m)}`, { r });
  }

  /** Honest note for flood and air: what the score cannot see, and when the map data behind it is missing. */
  function dataGapNote(place, m) {
    if (!place || !place[m]) return '';
    const unknown = place[m].factors.some((f) => f.value === null && (f.key === 'river' || f.key === 'traffic') && Math.round(f.score) === 50);
    return h('div', { class: 'banner small' }, icon('info', 'sm'), h('span', null, t(unknown ? `place.${m}Unknown` : `place.${m}Note`)));
  }

  /** Honest note about thin water data when the nearest drinking-water point is far or missing. */
  function waterNote(place) {
    const count = ctx.features ? ctx.features.filter((f) => f.key === 'water').length : null;
    const water = place?.heat.factors.find((f) => f.key === 'water');
    if (!place || !water || water.score >= 35 || !count) return '';
    return h('div', { class: 'banner small' }, icon('info', 'sm'), h('span', null, t('place.waterGap', { n: count })));
  }

  /** "Where to cool down / safe places nearby": from the API answer, or from the saved feature list offline. */
  function nearbyBlock(sel, place) {
    const keys = RELIEF[mode()];
    const nearest = place ? place.nearest.filter((n) => keys.includes(n.key))
      : ctx.features ? nearestFromFeatures(ctx.features, sel.lat, sel.lon, keys) : [];
    return h('section', null, h('h3', null, t(`place.near.${asMode(mode())}`)),
      nearest.length ? h('div', null, nearest.map(nearRow)) : h('p', { class: 'small muted' }, t('place.nothingNear', { r: formatDistance(searchRadius()) })));
  }

  function ring(score, band, label) {
    const r = 34, circ = 2 * Math.PI * r;
    const off = circ * (1 - clamp(score, 0, 100) / 100);
    const el = h('div', { class: 'ring', role: 'img', 'aria-label': `${label}: ${Math.round(score)} / 100, ${t(`band.${band}`)}` });
    el.innerHTML = `<svg viewBox="0 0 84 84" aria-hidden="true"><circle class="track" cx="42" cy="42" r="${r}" fill="none" stroke-width="9"/><circle class="val" cx="42" cy="42" r="${r}" fill="none" stroke-width="9" stroke-linecap="round" stroke-dasharray="${circ}" stroke-dashoffset="${off}" style="stroke:${BAND_FILL[band]}"/></svg>`;
    el.append(h('div', { class: 'center' }, h('b', null, Math.round(score)), h('small', null, '/100')));
    return el;
  }

  /** One score: the number, its band in words, which way is good, and a "!" that explains it with this place's numbers. */
  function scoreBox(kind, layer, big, place) {
    const band = layer.band;
    return h('div', { class: `score-box ${big ? 'active' : ''}`, title: scoreSummary(kind, layer.score, band) },
      h('div', { class: 'grow' },
        h('div', { class: 'lbl' }, icon(LAYERS[kind].icon, 'sm'), t(`mode.${kind}.score`),
          infoButton(() => openScoreExplainer({ layer: kind, place, meta: ctx.grid?.grid }), `${t('explain.how')} ${t(`mode.${kind}.score`)}`)),
        h('div', { class: 'row' }, h('span', { class: 'big num' }, Math.round(layer.score)), h('span', { class: 'band', 'data-band': band }, bandText(band, kindOf(kind)))),
        h('div', { class: 'tiny muted' }, t(`explain.dir.${kind}`)),
        h('div', { class: 'meter', 'data-band': band, style: { marginTop: '.4rem' }, role: 'presentation' }, h('span', { style: { width: `${clamp(layer.score, 0, 100)}%` } }))));
  }

  /** A factor of the score. Clicking it explains the factor: weight, why, data source and how many are mapped. */
  function factorRow(f, layerKey) {
    const band = bandOf(f.score, ctx.grid?.grid);
    const valueText = factorValue(f);
    const effect = t(layerKey === 'safety' ? 'explain.addsSafety' : `explain.adds.${layerKey}`, { n: Math.round(f.contribution * 10) / 10, max: f.weight });
    return h('button', { class: 'factor', type: 'button', 'data-fk': `factor-${layerKey}-${f.key}`, title: `${t(`factor.${f.key}`)}: ${valueText} · ${effect} · ${t('explain.click')}`,
      onclick: () => openFactorExplainer(f.key, { factor: f, layer: layerKey, meta: ctx.grid?.grid }) },
      h('span', { class: `chip ${FACTOR_LAYER[f.key] || 'heat'}`, style: { padding: '.15rem' } }, icon(FACTOR_ICON[f.key] || 'info', 'sm')),
      h('div', null, h('div', { style: { fontWeight: 600 } }, t(`factor.${f.key}`)), f.nearestName ? h('div', { class: 'tiny muted truncate' }, f.nearestName) : h('div', { class: 'tiny muted' }, effect)),
      h('div', { class: 'val' }, valueText),
      h('div', { class: 'meter', 'data-band': band, role: 'presentation' }, h('span', { style: { width: `${clamp(f.score, 0, 100)}%` } })));
  }

  function factorValue(f) {
    return f.unit === 'm' ? (f.value === null ? t(f.score === 50 && (f.key === 'river' || f.key === 'traffic') ? 'factor.unknown' : f.key === 'river' || f.key === 'traffic' ? 'factor.noneAway' : 'factor.none', { r: formatDistance(searchRadius()) }) : formatDistance(f.value)) : `${Math.round(f.value ?? 0)} ${t('factor.lamps')}`;
  }

  function nearRow(n) {
    const layer = FACTOR_LAYER[n.key] || 'heat';
    return h('div', { class: 'near' },
      h('div', { class: `ico ${layer}` }, icon(FACTOR_ICON[n.key] || 'pin')),
      h('div', { class: 'grow' },
        h('div', { style: { fontWeight: 600 } }, n.name || t(`kind.${n.kind}`)),
        h('div', { class: 'small muted' }, `${t(`factor.${n.key}`)} · ${formatDistance(n.distanceMeters)} · ${t('place.walkMin', { n: n.walkingMinutes })}${n.openingHours ? ` · ${n.openingHours}` : ''}`),
        h('div', { class: 'small' }, wheelchairBadge(n.wheelchair))),
      h('a', { class: 'btn icon quiet', href: directionsLink(n.latitude, n.longitude), target: '_blank', rel: 'noopener', 'aria-label': `${t('place.directions')}: ${n.name || t(`kind.${n.kind}`)}` }, icon('external', 'sm')));
  }

  function reportRow(r) {
    const confirmed = new Set(JSON.parse(localStorage.getItem('confirmed') || '[]'));
    const mine = confirmed.has(r.id);
    return h('li', { class: 'row between wrap' },
      h('div', null, h('div', { style: { fontWeight: 600 } }, t(`rtype.${r.type}`)),
        h('div', { class: 'small muted' }, `${t('report.supporters', { n: r.supporters })} · ${timeAgo(r.lastActivityAt, t, getLang())}${r.verifiedByPlanner ? ' · ' + t('report.verified') : ''}`)),
      h('button', { class: 'btn sm', type: 'button', 'data-fk': `confirm-${r.id}`, disabled: mine || isOffline() ? true : null, onclick: async (e) => {
        e.currentTarget.disabled = true;
        try {
          await confirmReport(r.id);
          confirmed.add(r.id);
          localStorage.setItem('confirmed', JSON.stringify([...confirmed].slice(-100)));
          toast(t('report.thanksConfirm'));
          loadGrid();
          selectPoint(ctx.selected.lat, ctx.selected.lon, { keepView: true, label: ctx.selected.label });
        } catch (err) { toast(errorText(err, t), { error: true }); e.currentTarget.disabled = false; }
      } }, mine ? t('report.confirmed') : t('report.stillTrue')));
  }

  function placeActions(sel) {
    return h('div', { class: 'row wrap' },
      h('button', { class: 'btn primary', type: 'button', 'data-fk': 'pa-report', onclick: () => showView('report') }, icon('flag', 'sm'), t('place.report')),
      h('button', { class: 'btn', type: 'button', 'data-fk': 'pa-walk', onclick: () => showView('walk', { to: [sel.lat, sel.lon], toLabel: sel.label }) }, icon('walk', 'sm'), t(`place.walkTo.${mode()}`)),
      h('button', { class: 'btn', type: 'button', 'data-fk': 'pa-area', onclick: () => { set({ me: { lat: sel.lat, lon: sel.lon, source: 'manual' } }); ctx.alerts?.refresh(); toast(t('place.areaSet')); } }, icon('bell', 'sm'), t('place.setArea')),
      h('a', { class: 'btn', href: directionsLink(sel.lat, sel.lon), target: '_blank', rel: 'noopener', 'data-fk': 'pa-directions' }, icon('external', 'sm'), t('place.directions')));
  }

  // ── Locate ─────────────────────────────────────────────────────────────────
  function locateMe(asArea = false) {
    if (!('geolocation' in navigator)) { toast(t('locate.unsupported'), { error: true }); return; }
    locateBtn.disabled = true;
    navigator.geolocation.getCurrentPosition((pos) => {
      locateBtn.disabled = false;
      const { latitude: lat, longitude: lon } = pos.coords;
      if (lat < 49.9 || lat > 50.2 || lon < 19.7 || lon > 20.3) { toast(t('locate.outside'), { error: true }); return; }
      set({ me: { lat, lon, source: 'gps' } });
      ctx.alerts?.refresh();
      selectPoint(lat, lon, { fly: true });
      if (asArea) toast(t('place.areaSet'));
    }, (err) => {
      locateBtn.disabled = false;
      toast(err.code === 1 ? t('locate.denied') : t('locate.failed'), { error: true });
    }, { enableHighAccuracy: true, timeout: 12000, maximumAge: 60000 });
  }
  ctx.locateMe = locateMe;

  // ── Alerts ─────────────────────────────────────────────────────────────────
  // The poll runs every minute: never rebuild the menu under the keyboard (the alert banners on the map still update).
  ctx.alerts = startAlerts(ctx, alertStack, () => { if (ctx.view === 'home' && !panelBody.contains(document.activeElement)) softRender(); });
  cleanups.push(() => ctx.alerts.stop());

  // ── Welcome ────────────────────────────────────────────────────────────────
  function welcome() {
    openDialog((close) => ({
      title: t('welcome.title'),
      body: h('div', { class: 'stack' },
        h('p', null, t('welcome.p1')),
        h('ul', { class: 'list' },
          h('li', { class: 'row' }, icon('moon'), h('span', null, t('welcome.safety'))),
          h('li', { class: 'row' }, icon('sun'), h('span', null, t('welcome.heat'))),
          h('li', { class: 'row' }, icon('wave'), h('span', null, t('welcome.flood'))),
          h('li', { class: 'row' }, icon('wind'), h('span', null, t('welcome.air'))),
          h('li', { class: 'row' }, h('span', { class: 'ico', 'aria-hidden': 'true' }, '♿'), h('span', null, t('welcome.access'))),
          h('li', { class: 'row' }, icon('flag'), h('span', null, t('welcome.report'))),
          h('li', { class: 'row' }, icon('offline'), h('span', null, t('welcome.offline')))),
        h('p', { class: 'banner info small' }, icon('info'), h('span', null, t('how.notCrime')))),
      footer: [h('button', { class: 'btn primary', type: 'button', onclick: () => close('ok') }, t('welcome.start'))]
    }), { onClose: () => set({ welcomed: true }) });
  }

  // ── Boot ───────────────────────────────────────────────────────────────────
  (async function init() {
    renderView();
    const [g, c, f, ty] = await Promise.all([peek('grid:all'), peek('conditions'), peek('features'), peek('report-types')]);
    if (c) { ctx.conditions = c.data; ctx.conditionsStale = true; if (!state.mode) set({ mode: asMode(c.data.suggestedMode) }); paintConditions(); }
    if (f) { ctx.features = f.data; ctx.places?.draw(); }
    if (ty) ctx.reportTypes = ty.data;
    if (g) applyGrid(g.data, g.savedAt, true);
    if (!state.mode) set({ mode: 'safety' });
    renderView();
    await Promise.all([loadConditions(), loadGrid(), loadFeatures(), loadTypes()]);
    if (!state.welcomed) welcome();
    flushOutbox();
  })();

  cleanups.push(on('apiOk', (ok) => {
    if (ok) { loadGrid(); loadConditions(); loadFeatures(); flushOutbox(); ctx.alerts.refresh(); }
    softRender();
  }));
  const refreshTimer = setInterval(() => { if (!document.hidden && !isOffline()) { loadGrid(); loadConditions(); } }, 5 * 60 * 1000);
  cleanups.push(() => clearInterval(refreshTimer));

  return () => cleanups.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });
}
