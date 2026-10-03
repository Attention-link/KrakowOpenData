// Shared planner pieces: data loading with offline fallback, the drill-down drawer for one area, report actions,
// and the "contact an agency" form with a generated brief.

import { h, icon, clear, toast, openDialog, timeAgo, copyText, formatDistance, geoLink } from './util.js';
import { state, isOffline } from './state.js';
import { t, tIn, getLang } from './i18n.js';
import { cachedGet, getSummary, getCell, getReports, getAgencies, getDispatches, getPlannerAlerts, getGrid, resolveReport, verifyReport, createDispatch, errorText } from './api.js';
import { gauge } from './charts.js';
import { addressLine } from './geo.js';
import { bandOf, kindOf, FACTOR_ICON, FACTOR_LAYER, RELIEF, LAYERS, modeOfEvent } from './model.js';
import { explainable, openFactorExplainer, openKpiExplainer, openScoreExplainer, bandText } from './explain.js';

// ── Shared planner state and a tiny event bus ────────────────────────────────
export const P = {
  root: null,          // the .pl-col element (drawers attach here)
  event: state.event,  // 'heat' | 'night' | 'both'
  summary: null,
  grid: null,
  focus: null,         // {lat, lon, cellId} the map should open on
  listeners: new Map()
};

export const pOn = (name, fn) => {
  if (!P.listeners.has(name)) P.listeners.set(name, new Set());
  P.listeners.get(name).add(fn);
  return () => P.listeners.get(name).delete(fn);
};
export const pEmit = (name, v) => P.listeners.get(name)?.forEach((fn) => { try { fn(v); } catch (e) { console.error(e); } });

// ── Loaders (network first, saved copy when offline) ─────────────────────────
export const loadSummary = () => cachedGet(`p:summary:${P.event}`, () => getSummary(P.event, 50));
export const loadGridFor = (event = P.event) => cachedGet(`p:grid:${event}`, () => getGrid(event));
export const loadReports = () => cachedGet('p:reports', () => getReports({ includeResolved: true, limit: 500 }, true));
export const loadAlerts = () => cachedGet('p:alerts', () => getPlannerAlerts(true));
export const loadAgencies = () => cachedGet('p:agencies', getAgencies);
export const loadDispatches = () => cachedGet('p:dispatches', getDispatches);

/** After any change on the server (report verified, alert sent, …): tell every open page to reload. */
export const dataChanged = () => pEmit('data');

/** Actions that change the server need a connection. */
export function requireOnline() {
  if (!isOffline()) return true;
  toast(t('pl.needsOnline'), { error: true });
  return false;
}

export function staleBanner(savedAt) {
  return h('div', { class: 'banner small', role: 'status' }, icon('offline', 'sm'),
    h('span', null, t('pl.savedData', { when: timeAgo(savedAt, t, getLang()) })));
}

// ── Agency choice per suggested action ───────────────────────────────────────
const AGENCY_FOR_ACTION = {
  ADD_WATER_POINT: 'crisis', ADD_SHADE: 'zzm', OPEN_COOL_SPACE: 'crisis', ADD_TOILET: 'portal', REVIEW_STOPS: 'ztp',
  FIX_LIGHTING: 'zdmk', REVIEW_NIGHT_SERVICE: 'ztp', ADD_NIGHT_PRESENCE: 'straz', ADD_AED: 'portal', REVIEW_REPORTS: 'portal'
};
export const agencyForAction = (code) => AGENCY_FOR_ACTION[code] || 'portal';

// ── Drawer ───────────────────────────────────────────────────────────────────
let current = null;

export function closeDrawer() {
  current?.remove();
  current?.scrim?.remove();
  current = null;
}

export function openDrawer({ title, build, footer }) {
  closeDrawer();
  const scrim = h('div', { class: 'scrim', onclick: closeDrawer });
  const body = h('div', { class: 'dr-body' });
  const drawer = h('aside', { class: 'drawer', role: 'dialog', 'aria-modal': 'false', 'aria-label': title },
    h('div', { class: 'dr-head' }, h('h2', { class: 'grow', tabindex: '-1' }, title),
      h('button', { class: 'btn icon quiet', type: 'button', 'aria-label': t('common.close'), onclick: closeDrawer }, icon('x'))),
    body, footer ? h('div', { class: 'dr-foot' }, footer) : '');
  drawer.scrim = scrim;
  P.root.append(scrim, drawer);
  current = drawer;
  drawer.querySelector('h2').focus({ preventScroll: true });
  const onKey = (e) => { if (e.key === 'Escape') { closeDrawer(); document.removeEventListener('keydown', onKey); } };
  document.addEventListener('keydown', onKey);
  build(body, drawer);
  return { drawer, body };
}

// ── Cell drill-down ──────────────────────────────────────────────────────────
export async function openCellDrawer(cellId, { meta } = {}) {
  const footer = [];
  const { body } = openDrawer({
    title: t('cell.title', { id: cellId }),
    build: (b) => b.append(h('div', { class: 'stack' }, h('div', { class: 'skeleton', style: { height: '90px' } }), h('div', { class: 'skeleton', style: { height: '200px' } }))),
    footer
  });
  const drawer = P.root.querySelector('.drawer');

  let res;
  try {
    res = await cachedGet(`p:cell:${cellId}:${P.event}`, () => getCell(cellId, P.event, true));
  } catch (e) {
    clear(body);
    body.append(h('div', { class: 'banner danger' }, icon('alert'), h('span', null, errorText(e, t))));
    return;
  }
  if (current !== drawer) return; // closed or replaced while loading
  const c = res.data;
  const gm = meta || P.grid?.grid;
  drawer.querySelector('h2').textContent = c.label ? `${t('cell.near', { name: c.label })} · ${cellId}` : t('cell.title', { id: cellId });
  clear(body);

  const stack = h('div', { class: 'stack' });
  // The drawer follows the planning event: it shows the one layer being planned for (heat, night safety, flood or air).
  const layers = [modeOfEvent(P.event)];
  const wantedLayer = new Set(layers.map((k) => LAYERS[k].api));
  const reportsHere = c.reports.filter((r) => wantedLayer.has(r.layer));
  if (res.stale) stack.append(staleBanner(res.savedAt));
  stack.append(
    h('div', { class: 'row between wrap' },
      h('div', { class: 'stack tight' }, addressLine(c.latitude, c.longitude, { fallback: c.label ? t('cell.near', { name: c.label }) : null }),
        h('span', { class: 'small muted' }, icon('target', 'sm'), ' ', t('cov.square', { r: formatDistance(gm?.cellSizeMeters || 250) }))),
      explainable(h('span', { class: 'chip accent' }, `${t('cell.priority')} ${Math.round(c.priority)}`), () => openKpiExplainer('priority', { value: c.priority, extra: [[t('cell.exposureLabel'), `${Math.round(c.exposure * 100)} / 100`], [t('mode.heat.score'), Math.round(c.heat)], [t('mode.safety.score'), Math.round(c.safety)], [t('mode.both.score'), Math.round(c.combined)]] }), t('cell.priority'))),
    h('div', { class: 'row wrap', style: { justifyContent: 'space-around' } },
      layers.map((k) => gaugeBox(c[k].score, gm, t(`mode.${k}.score`), kindOf(k), () => openScoreExplainer({ layer: k, place: c, meta: gm })))));

  // Why: the full factor tables (transparency), only for the layers of the event
  stack.append(h('section', null, h('h3', null, t('cell.why')),
    layers.map((k) => [k, c[k]]).map(([k, l]) => h('div', { style: { marginTop: '.6rem' } },
      explainable(h('p', { class: 'sect-title' }, k === 'heat'
        ? `${t('mode.heat.score')} · ${t('cell.base')} ${Math.round(l.baseScore)}${l.reportPenalty ? ` + ${l.reportPenalty} ${t('place.fromReports')}` : ''} = ${Math.round(l.score)} (${t('explain.dir.heat')})`
        : `${t(`mode.${k}.score`)} · ${t('cell.base')} ${Math.round(l.baseScore)}${l.reportPenalty ? ` − ${l.reportPenalty} ${t(k === 'safety' ? 'place.fromReports' : 'place.fromReportsLive')}` : ''} = ${Math.round(l.score)} (${t(`explain.dir.${k}`)})`),
      () => openScoreExplainer({ layer: k, place: c, meta: gm }), t(`mode.${k}.score`)),
      h('div', { class: 'tbl-wrap' }, h('table', { class: 't' },
        h('thead', null, h('tr', null, [t('cell.factor'), t('cell.weight'), t('cell.value'), t('cell.score'), k === 'heat' ? t('explain.colHeat') : k === 'safety' ? t('explain.colSafety') : t(`explain.col.${k}`)].map((x) => h('th', { scope: 'col' }, x)))),
        h('tbody', null, l.factors.map((f) => {
          const open = () => openFactorExplainer(f.key, { factor: f, layer: k, meta: gm });
          return h('tr', null,
            h('td', null, explainable(h('span', { class: 'row' }, icon(FACTOR_ICON[f.key] || 'info', 'sm'), t(`factor.${f.key}`)), open, t(`factor.${f.key}`))),
            h('td', { class: 'num' }, explainable(h('span', null, f.weight), open, t('cell.weight'))),
            h('td', { class: 'num' }, explainable(h('span', null, f.unit === 'm' ? (f.value === null ? t('factor.none', { r: formatDistance(gm?.searchRadiusMeters || 1500) }) : formatDistance(f.value)) : `${Math.round(f.value ?? 0)} ${t('factor.lamps')}`), open, t('cell.value'))),
            h('td', null, explainable(h('span', { class: 'band', 'data-band': bandOf(f.score, gm) }, Math.round(f.score)), open, t('cell.score'))),
            h('td', { class: 'num' }, explainable(h('span', null, `+${f.contribution}`), open, k === 'heat' ? t('explain.colHeat') : k === 'safety' ? t('explain.colSafety') : t(`explain.col.${k}`))));
        })))))),
    h('p', { class: 'tiny muted' }, t('cell.exposure', { n: Math.round(c.exposure * 100) }))));

  // Reports in this cell (of the event's layers)
  if (reportsHere.length) {
    stack.append(h('section', null, h('h3', null, t('cell.reports', { n: reportsHere.length })),
      h('ul', { class: 'list' }, reportsHere.map((r) => reportItem(r, () => openCellDrawer(cellId, { meta: gm }))))));
  }

  // Actions
  const brief = (a) => ({ cell: c, action: a });
  stack.append(h('section', null, h('h3', null, t('cell.actions')),
    c.actions.length ? h('ul', { class: 'list' }, c.actions.map((a) => h('li', null,
      h('div', { class: 'row between wrap' },
        h('div', { class: 'grow' }, h('b', null, t(`action.${a.code}`)),
          h('div', { class: 'small muted' }, h('span', { class: `chip ${a.severity === 'high' ? 'danger' : 'warn'}` }, t(`sev.${a.severity}`)), ' ', a.layer === 'Both' ? '' : t(`mode.${a.layer.toLowerCase()}`))),
        h('div', { class: 'row wrap' },
          h('button', { class: 'btn sm', type: 'button', onclick: () => openDispatchComposer({ cell: c, action: a }) }, icon('send', 'sm'), t('cell.contact')),
          h('button', { class: 'btn sm', type: 'button', onclick: () => openAlertDialog({ cell: c, action: a }) }, icon('bell', 'sm'), t('cell.alert'))))))) : h('p', { class: 'small muted' }, t('cell.noActions'))));

  // Nearest assets (of the event's layers)
  const nearestShown = c.nearest.filter((n) => RELIEF[modeOfEvent(P.event)].includes(n.key));
  if (nearestShown.length) {
    stack.append(h('section', null, h('h3', null, t('cell.nearest')),
      h('div', { class: 'tbl-wrap' }, h('table', { class: 't' }, h('tbody', null, nearestShown.map((n) => h('tr', null,
        h('td', null, h('span', { class: 'row' }, icon(FACTOR_ICON[n.key] || 'pin', 'sm'), t(`factor.${n.key}`))),
        h('td', null, n.name || t(`kind.${n.kind}`)),
        h('td', { class: 'num' }, `${formatDistance(n.distanceMeters)} · ${t('place.walkMin', { n: n.walkingMinutes })}`))))))));
  }
  body.append(stack);

  // Footer buttons
  const foot = drawer.querySelector('.dr-foot') || h('div', { class: 'dr-foot' });
  clear(foot);
  foot.append(
    h('button', { class: 'btn', type: 'button', onclick: () => { P.focus = { lat: c.latitude, lon: c.longitude, cellId }; location.hash = '#/planner/map'; } }, icon('map', 'sm'), t('cell.showMap')),
    h('button', { class: 'btn', type: 'button', onclick: () => openAlertDialog({ cell: c }) }, icon('bell', 'sm'), t('cell.alertHere')),
    h('button', { class: 'btn primary', type: 'button', onclick: () => openDispatchComposer({ cell: c }) }, icon('send', 'sm'), t('cell.contactAgency')));
  if (!foot.parentNode) drawer.append(foot);
}

function gaugeBox(score, meta, label, kind = 'good', onOpen = null) {
  const box = h('div', { style: { textAlign: 'center' } }, gauge(score, meta, label, kind), h('div', { class: 'tiny muted' }, label), h('div', { class: 'tiny muted' }, bandText(bandOf(score, meta, kind), kind)));
  return onOpen ? explainable(box, onOpen, label) : box;
}

// ── Reports: one row with planner actions ────────────────────────────────────
export function reportItem(r, onChange) {
  const demo = r.note?.startsWith('DEMO');
  return h('li', null,
    h('div', { class: 'row between wrap' },
      h('div', { class: 'grow' },
        h('b', null, t(`rtype.${r.type}`)), ' ', demo ? h('span', { class: 'tag-demo' }, 'DEMO') : null,
        h('div', { class: 'small muted' }, `${t('report.supporters', { n: r.supporters })} · ${timeAgo(r.lastActivityAt, t, getLang())} · ${r.status === 'Resolved' ? t('rep.resolved') : r.verifiedByPlanner ? t('report.verified') : t('rep.unverified')}`),
        r.note && !demo ? h('p', { class: 'small', style: { marginTop: '.25rem' } }, r.note) : null,
        r.resolutionNote ? h('p', { class: 'small muted' }, `${t('rep.resolution')}: ${r.resolutionNote}`) : null),
      r.status === 'Open' ? h('div', { class: 'row wrap' },
        r.verifiedByPlanner ? null : h('button', { class: 'btn sm', type: 'button', onclick: async () => { if (!requireOnline()) return; try { await verifyReport(r.id); toast(t('rep.verifiedToast')); dataChanged(); onChange?.(); } catch (e) { toast(errorText(e, t), { error: true }); } } }, icon('check', 'sm'), t('rep.verify')),
        h('button', { class: 'btn sm', type: 'button', onclick: () => resolveDialog(r, onChange) }, t('rep.resolve'))) : null));
}

export function resolveDialog(r, onChange) {
  if (!requireOnline()) return;
  openDialog((close) => {
    const note = h('textarea', { maxlength: '200', rows: '3', placeholder: t('rep.resolvePlaceholder') });
    return {
      title: t('rep.resolveTitle'),
      body: h('div', { class: 'stack' }, h('p', null, `${t(`rtype.${r.type}`)}`), h('label', { class: 'field' }, t('rep.resolveNote'), note)),
      footer: [h('button', { class: 'btn', type: 'button', onclick: () => close('c') }, t('common.cancel')),
        h('button', { class: 'btn primary', type: 'button', onclick: async () => {
          try { await resolveReport(r.id, note.value.trim() || null); toast(t('rep.resolvedToast')); close('ok'); dataChanged(); onChange?.(); }
          catch (e) { toast(errorText(e, t), { error: true }); }
        } }, t('rep.resolve'))]
    };
  });
}

// ── Brief for an agency ──────────────────────────────────────────────────────
export function makeBrief(lang, { cell, action }) {
  const L = (k, p) => tIn(lang, k, p);
  const band = (s, kind = 'good') => L(kind === 'heat' ? `band.heat.${bandOf(s, P.grid?.grid, 'heat')}` : `band.${bandOf(s, P.grid?.grid)}`);
  const weak = [...cell.safety.factors, ...cell.heat.factors, ...(cell.flood?.factors || []), ...(cell.air?.factors || [])].filter((f) => f.score < 35)
    .map((f) => `${L(`factor.${f.key}`)} (${f.unit === 'm' ? (f.value === null ? L('factor.none') : formatDistance(f.value)) : `${Math.round(f.value ?? 0)} ${L('factor.lamps')}`})`);
  const reports = cell.reports.filter((r) => r.status === 'Open');
  const c = P.summary?.data?.conditions || null;
  const lines = [
    L('brief.location', { lat: cell.latitude.toFixed(5), lon: cell.longitude.toFixed(5), cell: cell.cellId }),
    geoLink(cell.latitude, cell.longitude),
    c ? L('brief.situation', { heat: L(`heat.${c.heat.pressure}`), dark: c.isDark ? L('brief.dark') : L('brief.daylight') }) : null,
    L('brief.scores', { safety: Math.round(cell.safety.score), sb: band(cell.safety.score), heat: Math.round(cell.heat.score), hb: band(cell.heat.score, 'heat'), priority: Math.round(cell.priority) }),
    cell.flood && cell.air ? L('brief.floodAir', { flood: Math.round(cell.flood.score), fb: band(cell.flood.score), air: Math.round(cell.air.score), ab: band(cell.air.score) }) : null,
    weak.length ? L('brief.weak', { list: weak.join('; ') }) : null,
    reports.length ? L('brief.reports', { n: reports.length, types: [...new Set(reports.map((r) => L(`rtype.${r.type}`)))].join('; ') }) : null,
    action ? L('brief.request', { text: L(`action.${action.code}`) }) : null,
    '',
    L('brief.source')
  ];
  return lines.filter((x) => x !== null).join('\n');
}

// ── Contact an agency ────────────────────────────────────────────────────────
export async function openDispatchComposer({ cell, action, agencyId } = {}) {
  if (!requireOnline()) return;
  let agencies;
  try { agencies = (await loadAgencies()).data; } catch (e) { toast(errorText(e, t), { error: true }); return; }

  openDialog((close) => {
    let lang = 'pl'; // agencies write and read Polish; the planner can switch to English
    const pickedId = agencyId || (action ? agencyForAction(action.code) : agencies[0]?.id);
    const agencySel = h('select', { id: 'dsp-agency' }, agencies.map((a) => h('option', { value: a.id, selected: a.id === pickedId }, a.name)));
    const subject = h('input', { type: 'text', maxlength: '120', id: 'dsp-subject' });
    const bodyEl = h('textarea', { rows: '11', maxlength: '2000', id: 'dsp-body' });
    const langSel = h('select', null, [['pl', 'Polski'], ['en', 'English']].map(([v, l]) => h('option', { value: v, selected: v === lang }, l)));
    const info = h('div', { class: 'card flat small' });

    const fillSubject = () => {
      subject.value = cell ? tIn(lang, 'brief.subject', { cell: cell.cellId, action: action ? tIn(lang, `action.${action.code}`) : tIn(lang, 'brief.generic') }) : '';
    };
    const fillBody = () => { if (cell) bodyEl.value = makeBrief(lang, { cell, action }); };
    const paintAgency = () => {
      const a = agencies.find((x) => x.id === agencySel.value);
      clear(info);
      if (!a) return;
      info.append(h('b', null, a.name), h('div', { class: 'muted' }, a.responsibility),
        h('div', { class: 'row wrap', style: { marginTop: '.4rem' } },
          a.phone ? h('a', { class: 'btn sm', href: `tel:${a.phone.replace(/\s/g, '')}` }, icon('phone', 'sm'), a.phone) : null,
          a.phone && !a.contactVerified ? h('span', { class: 'chip warn' }, t('ag.verify')) : null,
          a.url ? h('a', { class: 'btn sm', href: a.url, target: '_blank', rel: 'noopener' }, icon('external', 'sm'), t('ag.website')) : null));
    };
    langSel.addEventListener('change', () => { lang = langSel.value; fillSubject(); fillBody(); });
    agencySel.addEventListener('change', paintAgency);
    fillSubject(); fillBody(); paintAgency();

    const sendBtn = h('button', { class: 'btn primary', type: 'button' }, icon('send', 'sm'), t('ag.record'));
    sendBtn.addEventListener('click', async () => {
      sendBtn.disabled = true;
      try {
        const d = await createDispatch({ agencyId: agencySel.value, subject: subject.value, body: bodyEl.value, latitude: cell?.latitude ?? null, longitude: cell?.longitude ?? null, cellId: cell?.cellId ?? null });
        toast(t('ag.recorded', { ref: d.reference }), { ms: 9000 });
        dataChanged();
        close('ok');
      } catch (e) { toast(errorText(e, t), { error: true }); sendBtn.disabled = false; }
    });

    return {
      title: t('ag.title'),
      body: h('div', { class: 'stack' },
        h('p', { class: 'banner info small' }, icon('info', 'sm'), h('span', null, t('ag.simulated'))),
        h('label', { class: 'field' }, t('ag.agency'), agencySel), info,
        h('div', { class: 'row wrap' }, h('label', { class: 'field grow' }, t('ag.subject'), subject), h('label', { class: 'field' }, t('ag.language'), langSel)),
        h('label', { class: 'field' }, t('ag.message'), bodyEl)),
      footer: [h('button', { class: 'btn', type: 'button', onclick: async () => { if (await copyText(`${subject.value}\n\n${bodyEl.value}`)) toast(t('ag.copied')); } }, icon('copy', 'sm'), t('ag.copy')),
        h('button', { class: 'btn', type: 'button', onclick: () => close('c') }, t('common.cancel')), sendBtn]
    };
  });
}

// ── Alert composer in a dialog (the Alerts page embeds the same form) ────────
export async function openAlertDialog({ cell, action } = {}) {
  if (!requireOnline()) return;
  const { alertForm } = await import('./planner-alerts.js');
  openDialog((close) => ({
    title: t('al.new'),
    body: alertForm({ cell, action, onSent: () => { close('ok'); dataChanged(); } }),
    footer: null
  }));
}
