// Walk check along real streets. Night safety mode = a night walk (lighting, night transport, open places, reports);
// Heat mode = a cool walk (shade, water, cool places, reports); Flood = a walk away from the water; Air = a walk away from traffic.
// Pick the start (A) and the destination (B) by typing an address or stop, tapping the map, or using your area.
// The API finds the fastest street route and, when one scores clearly better, a safer / cooler / better one to compare with it.
// Offline (or when street routing is down) the straight line between the two points is estimated from the saved map.

import { h, icon, clear, formatDistance, haversine } from './util.js';
import { state, isOffline, eventForMode } from './state.js';
import { t } from './i18n.js';
import { getRoutes, errorText } from './api.js';
import { iconMarker, endMarker } from './map.js';
import { bandOf, kindOf, BAND_FILL, cellOf, COL, nearestFromFeatures, FACTOR_ICON } from './model.js';
import { searchBox } from './search.js';
import { addressLine, reverseLabel, coords } from './geo.js';
import { bandText, infoButton, openScoreExplainer } from './explain.js';

const PATH_ACCENT = { safety: '#4a3aa7', heat: '#b8480f', flood: '#0b5cad', air: '#0f6b5c' };   // = the walk button colours (night indigo, heat orange, flood blue, air teal)
const SCORE_KEY ={ safety: 'safety', heat: 'heat', flood: 'flood', air: 'air', both: 'combined' };
const HELP_KEYS = { safety: ['openPlaces', 'aed'], heat: ['water', 'green', 'refuge', 'toilets'], flood: ['emergency'], air: ['green', 'refuge'], both: ['water', 'green', 'openPlaces'] };

export function renderWalkView(ctx, body) {
  const ws = ctx.walkState ||= { from: null, to: null, fromLabel: null, toLabel: null, pick: null, routes: null, selected: 'fastest', loading: false, error: null, offline: false, token: 0 };
  const mode = ctx.mode();
  const key = SCORE_KEY[mode];
  const kind = kindOf(mode);
  const props = ctx.viewProps || {};
  if (props.to) { ws.to = props.to; ws.toLabel = props.toLabel || null; ctx.viewProps = null; }
  if (!ws.from && state.me) ws.from = [state.me.lat, state.me.lon];

  // The chosen start and end stay when the layer changes; the routes are found again for the new layer.
  if (ws.routes && ws.routesMode !== mode) ws.routes = null;

  const layer = ctx.walkLayer ||= L.layerGroup().addTo(ctx.map);
  ctx.walkCleanup = () => {
    layer.clearLayers();
    ctx.map.removeLayer(layer);
    ctx.walkLayer = null;
    ctx.pick = null;
    ctx.walkState = null;
    ctx.info.hide();
  };
  ctx.pick = (latlng) => {
    if (!ws.pick) return false;
    set(ws.pick, [latlng.lat, latlng.lng], null);
    ws.pick = null;
    return true;
  };

  const host = h('div', { class: 'stack' });
  body.append(host);
  ctx.setSheet('half');
  drawMap(false);
  if (ws.from && ws.to && !ws.routes && !ws.loading) compute();
  paint();

  /** Sets one end of the walk, resolves its address, and recomputes. */
  function set(which, ll, label) {
    ws[which] = ll;
    ws[`${which}Label`] = label;
    ws.routes = null;
    if (!label) reverseLabel(ll[0], ll[1]).then((l) => { if (ws[which] === ll) { ws[`${which}Label`] = l; showInfo(); paint(); } });
    drawMap(false);
    compute();
    paint();
  }

  const selectedRoute = () => (ws.routes ? (ws.selected === 'better' && ws.routes.better ? ws.routes.better : ws.routes.fastest) : null);

  // ── Map ────────────────────────────────────────────────────────────────────
  function drawMap(fit) {
    layer.clearLayers();
    drawRoutes(fit);
    if (ws.from) endMarker(ws.from, 'start', t('route.start')).addTo(layer);
    if (ws.to) endMarker(ws.to, 'end', t('route.destination')).addTo(layer);
    showInfo();
  }

  function showInfo() {
    if (!ws.from && !ws.to) { ctx.info.hide(); return; }
    const name = (ll, label) => label || (ll ? coords(ll[0], ll[1]) : '…');
    ctx.info.showText(`A ${name(ws.from, ws.fromLabel)} → B ${name(ws.to, ws.toLabel)}`, t('cov.walk', { r: formatDistance(ctx.grid?.grid?.searchRadiusMeters || 1500) }));
  }

  /** Route path cut into runs of the same band, so the colour follows the score along the street. */
  function bandRuns(route) {
    const meta = ctx.grid?.grid;
    const pts = route.path;
    const n = route.samples.length;
    const cum = [0];
    for (let i = 1; i < pts.length; i++) cum.push(cum[i - 1] + haversine(pts[i - 1], pts[i]));
    const total = cum[cum.length - 1] || 1;
    const runs = [];
    let cur = null;
    for (let i = 1; i < pts.length; i++) {
      const mid = (cum[i - 1] + cum[i]) / 2 / total;
      const s = route.samples[Math.min(n - 1, Math.round(mid * (n - 1)))];
      const band = bandOf(s[key], meta, kind);
      if (cur && cur.band === band) cur.pts.push(pts[i]);
      else { cur = { band, pts: [pts[i - 1], pts[i]] }; runs.push(cur); }
    }
    return runs;
  }

  function drawRoutes(fit) {
    const r = ws.routes;
    if (!r) return;
    const sel = selectedRoute();
    const all = [r.fastest, r.better].filter(Boolean);
    // The route that is not selected is a grey dashed line, so both can be compared on the map.
    for (const route of all) {
      if (route === sel) continue;
      L.polyline(route.path, { color: '#454a52', weight: 5, opacity: 0.85, dashArray: '2 9', lineCap: 'round' })
        .bindTooltip(`${t(`route.${route.kind}`)} · ${t('route.min', { n: route.walkingMinutes })}`, { sticky: true }).addTo(layer);
    }
    if (sel) {
      // Thin outer casing in the layer's CTA colour ties the route to the button; the band colours inside are unchanged.
      L.polyline(sel.path, { color: PATH_ACCENT[mode] || PATH_ACCENT.safety, weight: 15, opacity: 0.9, lineCap: 'round', lineJoin: 'round' }).addTo(layer);
      L.polyline(sel.path, { color: '#ffffff', weight: 11, opacity: 0.95, lineCap: 'round', lineJoin: 'round' }).addTo(layer);
      for (const run of bandRuns(sel)) {
        L.polyline(run.pts, { color: BAND_FILL[run.band], weight: 7, opacity: 1, lineCap: 'round', lineJoin: 'round' })
          .bindTooltip(`${t(`route.${sel.kind}`)} · ${bandText(run.band, kind)}`, { sticky: true }).addTo(layer);
      }
      const w = sel.samples[sel.weakestSampleIndex];
      if (w && bandOf(sel.worst, ctx.grid?.grid, kind) !== 'Good') iconMarker([w.latitude, w.longitude], 'alert', mode, { title: t(`walk.weakest.${mode}`), z: 600 }).addTo(layer);
    }
    if (fit) {
      const pts = all.flatMap((route) => route.path);
      if (pts.length) ctx.map.fitBounds(L.latLngBounds(pts).pad(0.3), { maxZoom: 17 });
    }
  }

  // ── Data ───────────────────────────────────────────────────────────────────
  async function compute() {
    if (!ws.from || !ws.to) return;
    const token = ++ws.token;
    ws.loading = true; ws.error = null; ws.routes = null; ws.offline = false;
    paint();
    try {
      if (isOffline()) throw new Error('offline');
      ws.routes = await getRoutes(ws.from, ws.to, eventForMode(mode));
      ws.routesMode = mode;
      ws.selected = ws.routes.better ? 'better' : 'fastest';
    } catch (e) {
      if (token !== ws.token) return;
      const approx = approximate();
      if (approx) { ws.routes = approx; ws.routesMode = mode; ws.offline = true; ws.selected = 'fastest'; }
      else ws.error = e.message === 'offline' ? t('err.offline') : errorText(e, t);
    }
    if (token !== ws.token) return;
    ws.loading = false;
    drawMap(true);
    paint();
  }

  /** The same idea from the saved grid: a straight line, 50 m samples, the scores of the cell each sample falls in. */
  function approximate() {
    if (!ctx.grid || !ctx.gridIndex) return null;
    const length = haversine(ws.from, ws.to);
    if (length > 5000) return null;
    const steps = Math.max(1, Math.ceil(length / 50));
    const samples = [];
    for (let i = 0; i <= steps; i++) {
      const f = i / steps;
      const lat = ws.from[0] + (ws.to[0] - ws.from[0]) * f, lon = ws.from[1] + (ws.to[1] - ws.from[1]) * f;
      const c = cellOf(ctx.grid.grid, lat, lon);
      const row = ctx.gridIndex.get(`${c.row}-${c.col}`);
      samples.push({ latitude: lat, longitude: lon, safety: row ? row[COL.safety] : 0, heat: row ? row[COL.heat] : 0, combined: row ? row[COL.combined] : 0, flood: row ? row[COL.flood] : 0, air: row ? row[COL.air] : 0 });
    }
    const vals = samples.map((s) => s[key]);
    const worstIdx = vals.indexOf(Math.min(...vals));
    const fastest = {
      kind: 'fastest', lengthMeters: Math.round(length), walkingMinutes: Math.ceil(length / 80), path: [ws.from, ws.to], samples,
      average: vals.reduce((a, v) => a + v, 0) / vals.length, worst: vals[worstIdx], weakestSampleIndex: worstIdx, openReportsNearby: 0
    };
    return { mode: eventForMode(mode), source: 'straight-line', fastest, better: null, betterKind: 'none', scoreGain: 0, extraMeters: 0, extraMinutes: 0, note: '' };
  }

  // ── Panel ──────────────────────────────────────────────────────────────────
  function endCard(which) {
    const value = ws[which];
    const picking = ws.pick === which;
    const box = searchBox({
      label: t(`route.${which}`), placeholder: t(`walk.${which}Placeholder`), filters: false, near: () => value || [ctx.map.getCenter().lat, ctx.map.getCenter().lng],
      initial: ws[`${which}Label`] || '', onPick: (r) => set(which, [r.lat, r.lon], r.label)
    });
    return h('div', { class: 'card flat stack tight' },
      box,
      value ? addressLine(value[0], value[1], { fallback: ws[`${which}Label`] }) : h('p', { class: 'small muted' }, t('walk.notSet')),
      h('div', { class: 'row wrap' },
        h('button', { class: `btn sm ${picking ? 'cta' : 'cta-soft'}`, 'data-layer': mode, type: 'button', onclick: () => { ws.pick = picking ? null : which; paint(); } }, icon('pin', 'sm'), picking ? t('walk.tapMap') : t('walk.pick')),
        state.me ? h('button', { class: 'btn sm cta-soft', 'data-layer': mode, type: 'button', onclick: () => set(which, [state.me.lat, state.me.lon], null) }, t('walk.useMine')) : null,
        ctx.selected ? h('button', { class: 'btn sm cta-soft', 'data-layer': mode, type: 'button', onclick: () => set(which, [ctx.selected.lat, ctx.selected.lon], ctx.selected.label || null) }, t('walk.useSelected')) : null));
  }

  function paint() {
    // The search boxes are rebuilt with the card, so keep it cheap: only repaint on real state changes.
    clear(host);
    host.append(h('p', null, t(`walk.intro.${mode}`)), endCard('from'), endCard('to'));
    if (ws.from && ws.to) {
      host.append(h('button', { class: 'btn sm cta-soft', 'data-layer': mode, type: 'button', onclick: () => {
        [ws.from, ws.to] = [ws.to, ws.from];
        [ws.fromLabel, ws.toLabel] = [ws.toLabel, ws.fromLabel];
        ws.routes = null; drawMap(false); compute(); paint();
      } }, icon('refresh', 'sm'), t('walk.swap')));
    }
    if (ws.loading) host.append(h('p', { class: 'small muted' }, t('route.loading')), h('div', { class: 'skeleton', style: { height: '120px' } }));
    if (ws.error) host.append(h('div', { class: 'banner danger' }, icon('alert'), h('span', null, ws.error)));
    if (ws.routes) host.append(comparison(ws.routes));
    host.append(h('p', { class: 'tiny muted' }, ws.routes && ws.routes.source === 'straight-line' ? (ws.offline ? t('route.offline') : t('route.straight')) : t('route.note')));
  }

  function select(which) {
    ws.selected = which;
    drawMap(false);
    paint();
  }

  function routeCard(route, which) {
    const meta = ctx.grid?.grid;
    const avgBand = bandOf(route.average, meta, kind);
    const worstBand = bandOf(route.worst, meta, kind);
    const pressed = selectedRoute() === route;
    const fastest = ws.routes.fastest;
    const label = t(`route.${route.kind}`);
    return h('button', { class: 'route-card', type: 'button', 'aria-pressed': String(pressed), onclick: () => select(which) },
      h('div', { class: 'row between wrap' },
        h('h3', null, h('span', { class: `swatch ${which === 'fastest' && ws.routes.better ? 'dashed' : 'solid'}` }), label),
        h('b', null, `${t('route.min', { n: route.walkingMinutes })} · ${formatDistance(route.lengthMeters)}`)),
      which === 'better'
        ? h('div', { class: 'small' }, t('route.extra', { m: formatDistance(Math.max(0, route.lengthMeters - fastest.lengthMeters)), n: Math.max(0, route.walkingMinutes - fastest.walkingMinutes) }))
        : null,
      h('div', { class: 'route-stats' },
        h('span', { class: 'band', 'data-band': avgBand }, t(`route.avg.${mode}`, { n: Math.round(route.average) }), ' · ', bandText(avgBand, kind)),
        h('span', { class: 'band', 'data-band': worstBand }, t(`route.worst.${mode}`, { n: Math.round(route.worst) })),
        route.openReportsNearby ? h('span', { class: 'chip warn' }, icon('flag', 'sm'), route.openReportsNearby) : null),
      h('div', { class: 'tiny muted', style: { marginTop: '.25rem' } }, pressed ? t('route.shown') : t('route.show')));
  }

  function comparison(r) {
    const meta = ctx.grid?.grid;
    const sel = selectedRoute();
    const worstBand = bandOf(sel.worst, meta, kind);
    const group = worstBand === 'Good' ? 'good' : worstBand === 'Fair' ? 'fair' : 'weak';
    const w = sel.samples[sel.weakestSampleIndex];
    const help = ctx.features && w ? nearestFromFeatures(ctx.features, w.latitude, w.longitude, HELP_KEYS[mode], 600) : [];
    const layerKey = mode;
    return h('div', { class: 'stack' },
      r.source === 'street' ? null : h('div', { class: 'banner small' }, icon(ws.offline ? 'offline' : 'info', 'sm'), h('span', null, ws.offline ? t('route.offline') : t('route.straight'))),
      r.widened ? h('div', { class: `banner small ${r.better ? 'info' : 'warn'}` }, icon('map', 'sm'),
        h('span', null, r.better ? t('route.widened', { m: r.extraMinutes }) : t('route.widenedNone'))) : null,
      h('div', { class: 'row between wrap' },
        h('b', null, t('route.advice')),
        h('span', { class: 'row small muted' }, t('legend.route'), infoButton(() => openScoreExplainer({ layer: layerKey, meta }), t('explain.how')))),
      routeCard(r.fastest, 'fastest'),
      r.better ? routeCard(r.better, 'better') : null,
      r.source === 'street'
        ? h('div', { class: `banner small ${r.better ? 'ok' : 'info'}` }, icon(r.better ? 'check' : 'info', 'sm'),
          h('span', null, r.better ? t(`route.better.${mode}`, { g: Math.round(r.scoreGain * 10) / 10 }) : t(`route.none.${mode}`)))
        : null,
      // Why only the fastest path is shown: it clears the city's thresholds for this measure (set by the planner).
      r.source === 'street' && !r.better && r.fastestIsAcceptable && r.thresholdAverage != null && ['safety', 'heat', 'flood', 'air'].includes(mode)
        ? h('p', { class: 'small muted' }, icon('info', 'sm'), ' ', t(`th.fastest.${mode}`, { avg: Math.round(r.fastest.average), worst: Math.round(r.fastest.worst), tavg: r.thresholdAverage, tworst: r.thresholdWorst }))
        : null,
      h('div', { class: 'card' },
        h('p', null, t(`walk.advice.${mode}.${group}`)),
        sel.openReportsNearby ? h('p', { class: 'small', style: { marginTop: '.4rem' } }, icon('flag', 'sm'), ' ', t('walk.reports', { n: sel.openReportsNearby })) : null),
      worstBand !== 'Good' && w
        ? h('div', { class: 'card flat stack tight' }, h('b', null, t(`walk.weakAt.${mode}`)), addressLine(w.latitude, w.longitude),
          help.length ? h('ul', { class: 'list small' }, help.map((n) => h('li', { class: 'row' }, icon(FACTOR_ICON[n.key] || 'pin', 'sm'),
            h('span', null, `${n.name || t(`kind.${n.kind}`)} · ${formatDistance(n.distanceMeters)}`)))) : h('p', { class: 'small muted' }, t('walk.noHelp')))
        : null);
  }
}
