// Planner side of the fifth layer, Accessibility: the profile (wheelchair | pram | limited mobility), the API calls that carry it,
// the explanations of its figures and the drawer sections for one square. Squares with no accessibility data are never scored:
// they are shown as "no data" (neutral grey, dashed outline, the words), counted separately and left out of every average.
//
// The shared core (model.js, api.js, state.js) belongs to the resident side; everything the planner needs from the access layer that
// might not be there yet is defined here, so this file works on its own.

import { h, icon, clear, formatDistance } from './util.js';
import { state, set } from './state.js';
import { t } from './i18n.js';
import { LAYERS, modeOfEvent, layerOfApi, REPORT_LAYER, FACTOR_ICON, BAND_FILL, bandOf, bandRange } from './model.js';
import { openFacts, loadMethod, bandText, explainable } from './explain.js';
import { gauge } from './charts.js';

export const PROFILES = ['wheelchair', 'pram', 'mobility'];
export const PROFILE_API = { wheelchair: 'Access', pram: 'AccessPram', mobility: 'AccessMobility' };
export const BASES = ['steps', 'kerbs', 'surface', 'slope', 'stepFree', 'accessStops', 'accessToilets', 'rest', 'tactile'];

// ── Profile: kept in the shared state when it has the field, and in the same storage key the resident side uses ──
const PROFILE_KEY = 'kk.access.profile';
let memoryProfile = null;

export function getProfile() {
  if (PROFILES.includes(state.accessProfile)) return state.accessProfile;
  try { const v = localStorage.getItem(PROFILE_KEY); if (PROFILES.includes(v)) return v; } catch { /* storage blocked */ }
  return memoryProfile || 'wheelchair';
}

export function saveProfile(profile) {
  memoryProfile = profile;
  try { localStorage.setItem(PROFILE_KEY, profile); } catch { /* storage blocked: kept for this visit */ }
  set({ accessProfile: profile });
}

// ── Layer helpers that do not depend on the core knowing the access layer yet ───────────────────────────────
const ACCESS_DEF = { mode: 'access', event: 'access', api: 'Access', icon: 'a11y', kind: 'good' };
/** The icon of the accessibility layer: the core's own when the sprite has it, else the generic accessibility icon. */
export const accessIcon = () => (document.getElementById('i-access') ? 'access' : 'a11y');
/** The layer key of a planner event: 'safety' | 'heat' | 'flood' | 'air' | 'access'. */
export const layerMode = (ev) => (ev === 'access' ? 'access' : modeOfEvent(ev));
export const layerDef = (mode) => LAYERS[mode] || (mode === 'access' ? ACCESS_DEF : LAYERS.safety);
export const isAccessEvent = (ev) => ev === 'access';
/** API layer name that reports and alerts use for an event ('Access' for every profile). */
export const apiLayerOf = (ev) => (ev === 'access' ? 'Access' : LAYERS[modeOfEvent(ev)].api);
/** The layer key of an API layer name; AccessPram and AccessMobility are the access layer too. */
export const layerKeyOfApi = (name) => (typeof name === 'string' && name.startsWith('Access') ? 'access' : layerOfApi(name));
/** Which layer a report type feeds. PathHazard is the accessibility layer's report. */
export const reportLayerOf = (type) => (type === 'PathHazard' ? 'access' : (REPORT_LAYER[type] || 'safety'));
/** Key that separates saved copies: the profile matters only for the access event. */
export const evKey = (ev, profile) => (ev === 'access' ? `access:${profile}` : ev);
/** Name of a layer, for a label: works for the API names too ('Heat', 'AccessPram', 'Both'). */
export const layerNameOfApi = (name) => (name === 'Both' || !name ? '' : t(`mode.${layerKeyOfApi(name) || name.toLowerCase()}`));

/** The score of a layer in a priority row or a place: access has its own field. */
export const scoreOfLayer = (obj, mode) => (mode === 'access' ? (obj.access?.score !== undefined ? obj.access.score : obj.access) : obj[mode]);

// ── Factors ──────────────────────────────────────────────────────────────────────────────────────────────────
export const isAccessKey = (key) => typeof key === 'string' && key.startsWith('access.');
const baseOf = (key) => key.split('.').pop();
const ACCESS_FACTOR_ICON = { steps: 'up', kerbs: 'down', surface: 'layers', slope: 'up', stepFree: 'building', accessStops: 'bus', accessToilets: 'toilet', rest: 'pin', tactile: 'target' };
export const factorLabel = (key) => (isAccessKey(key) ? t(`pa.factor.${baseOf(key)}`) : t(`factor.${key}`));
export const factorIcon = (key) => FACTOR_ICON[key] || (isAccessKey(key) ? ACCESS_FACTOR_ICON[baseOf(key)] : null) || 'info';
export const factorKey = (profile, base) => `access.${profile}.${base}`;

const one = (n) => Math.round(n * 10) / 10;
/** The measured value of an access factor with its unit: barriers (a weighted count), % of smooth paths, or a distance. */
export function accessValueText(f, meta) {
  if (f.hasData === false) return t('accl.noData');
  if (f.unit === 'barriers') return t('factor.barriers', { n: one(f.value ?? 0) });
  if (f.unit === '%') return f.value === null || f.value === undefined ? t('factor.notMapped') : t('factor.pctSmooth', { n: Math.round(f.value) });
  if (f.unit === 'm') return f.value === null || f.value === undefined ? t('factor.none', { r: formatDistance(meta?.searchRadiusMeters || 1500) }) : formatDistance(f.value);
  return String(f.value ?? '–');
}

// ── The profile switch shown under the event selector ──────────────────────────────────────────────────────────
/** {el, update(profile)}: three buttons and one line saying what the chosen profile emphasises. */
export function profileBar(initial, onPick) {
  let current = initial;
  const seg = h('div', { class: 'seg', role: 'group', 'aria-label': t('accl.profile.label') });
  const line = h('p', { class: 'small muted pl-profile-line', id: 'pa-profile-line' });
  const el = h('div', { class: 'pl-profile' }, h('span', { class: 'small', id: 'pa-profile-title' }, t('accl.profile.label')), seg, line);
  const paint = () => {
    clear(seg);
    for (const p of PROFILES) {
      seg.append(h('button', { type: 'button', 'data-profile': p, 'aria-pressed': String(p === current), 'aria-describedby': 'pa-profile-line', onclick: () => { if (p !== current) onPick(p); } }, t(`accl.p.${p}`)));
    }
    line.textContent = t(`accl.emph.${current}`);
  };
  paint();
  return { el, update(p) { current = p; paint(); } };
}

// ── No data ──────────────────────────────────────────────────────────────────────────────────────────────────
/** A prominent notice with the number of squares that have no accessibility data; click for the full explanation. */
export function noDataNotice(info, onOpen) {
  if (!info) return null;
  if (!info.hasData) return h('div', { class: 'banner warn nodata-banner' }, icon('info'), h('div', { class: 'grow' }, h('b', null, t('pa.nodata.none')), h('div', { class: 'small' }, t('pa.nodata.noneHelp'))));
  const n = info.cellsWithoutData;
  return h('div', { class: 'banner nodata-banner', role: 'status' }, icon('info'),
    h('div', { class: 'grow' }, h('b', null, t('pa.nodata.count', { n: n.toLocaleString() })), h('div', { class: 'small' }, t('pa.nodata.short', { with: info.cellsWithData.toLocaleString() })),
      onOpen ? h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: onOpen }, icon('info', 'sm'), t('pa.nodata.explainBtn')) : null));
}

export function openNoDataExplainer(info) {
  const a = info?.dataArea;
  openFacts({
    title: t('pa.nodata.title'),
    value: info ? info.cellsWithoutData.toLocaleString() : undefined,
    valueNote: t('pa.nodata.squares'),
    intro: t('pa.nodata.what'),
    sections: [
      h('dl', { class: 'kv explain-kv' },
        h('dt', null, t('pa.nodata.withData')), h('dd', null, info ? info.cellsWithData.toLocaleString() : '–'),
        h('dt', null, t('pa.nodata.area')), h('dd', null, a ? `${a.minLatitude ?? a.minLat}, ${a.minLongitude ?? a.minLon} – ${a.maxLatitude ?? a.maxLat}, ${a.maxLongitude ?? a.maxLon}` : t('pa.nodata.areaUnknown')),
        h('dt', null, t('pa.nodata.reasons')), h('dd', null, t('pa.nodata.reasonsList')),
        h('dt', null, t('pa.nodata.excluded')), h('dd', null, t('pa.nodata.excludedHelp')),
        h('dt', null, t('explain.source')), h('dd', null, t('pa.nodata.source'))),
      info?.note ? h('p', { class: 'tiny muted' }, info.note) : null
    ]
  });
}

// ── Explanations ─────────────────────────────────────────────────────────────────────────────────────────────
const unavailable = () => openFacts({ title: t('explain.title'), intro: t('explain.unavailable') });
const methodLayer = (m, profile) => m?.layers?.find((l) => l.layer === PROFILE_API[profile]) || null;

function bandList(L, meta) {
  return h('ul', { class: 'list small band-list' }, L.bands.map((b) => {
    const [from, to] = bandRange(b.band, meta, 'good');
    return h('li', { class: 'row', 'data-band': b.band },
      h('span', { class: 'band', 'data-band': b.band, style: { minWidth: '7.5rem', justifyContent: 'center' } }, `${Math.round(from)}–${Math.round(to)}`),
      h('span', null, h('b', null, bandText(b.band, 'good')), ' · ', b.meaning));
  }));
}

/** What the accessibility score of a profile means: the scale, the nine weighted factors with their reasons, and what no data means. */
export function openAccessScoreExplainer({ profile = getProfile(), place = null, meta = null } = {}) {
  loadMethod().then((m) => {
    const L = methodLayer(m, profile);
    if (!L) { unavailable(); return; }
    const data = place?.access || null;
    const has = data && data.hasData !== false && data.score !== null && data.score !== undefined;
    const sections = [
      h('section', { class: 'explain-sec' },
        h('p', { class: 'small' }, h('b', null, L.direction)),
        h('p', { class: 'small' }, L.meaning),
        place ? h('p', { class: 'explain-now' }, `${t('explain.thisPlace')}: `, has ? [h('b', null, `${Math.round(data.score)} / 100`), ' · ', bandText(data.band, 'good')] : h('b', null, t('accl.noData'))) : null,
        h('h4', null, t('explain.scale')), bandList(L, meta),
        h('p', { class: 'small' }, icon('info', 'sm'), ' ', t('pa.nodata.inScale')),
        h('h4', null, t('explain.weightsTitle')),
        ...L.factors.map((f) => h('div', { class: 'factor-doc' },
          h('div', { class: 'row between' }, h('b', { class: 'row' }, icon(factorIcon(f.key), 'sm'), factorLabel(f.key)), h('b', null, `${f.weight}`)),
          h('p', { class: 'small' }, f.whyWeighted))),
        h('h4', null, t('explain.formula')), h('p', { class: 'small' }, L.formula))
    ];
    openFacts({
      title: `${t('mode.access.score')} · ${t(`accl.p.${profile}`)}`,
      value: has ? Math.round(data.score) : undefined, valueNote: has ? '/ 100' : null,
      band: has ? data.band : undefined, bandKind: 'good',
      sections
    });
  }).catch(unavailable);
}

/** One accessibility factor: what it measures, its weight for the profile, the reason, the data source, and (with a place) the value there. */
export function openAccessFactor(key, { factor = null, meta = null } = {}) {
  loadMethod().then((m) => {
    const info = m?.layers?.flatMap((l) => l.factors).find((f) => f.key === key);
    if (!info) { openFacts({ title: factorLabel(key), intro: t('explain.unavailable') }); return; }
    const profile = key.split('.')[1];
    const rows = [
      [t('explain.measures'), info.measures],
      [t('explain.weight'), h('b', null, `${info.weight} / 100`)],
      [t('explain.fullScore'), info.fullScoreAt],
      [t('explain.zeroScore'), info.zeroScoreAt],
      [t('explain.why'), info.whyWeighted],
      [t('explain.source'), info.source],
      [t('explain.mapped'), (info.mappedCount ?? 0).toLocaleString()]
    ];
    const here = factor ? [
      [t('explain.hereValue'), accessValueText(factor, meta)],
      factor.hasData === false ? [t('explain.hereEffect'), t('accl.factorNoData')] : null,
      factor.hasData === false ? null : factor.nearestName ? [t('explain.hereNearest'), factor.nearestName] : null,
      factor.hasData === false ? null : [t('explain.hereScore'), `${Math.round(factor.score)} / 100`],
      factor.hasData === false ? null : [t('explain.hereEffect'), t('pa.adds', { n: one(factor.contribution), max: factor.weight })]
    ].filter(Boolean) : null;
    const kv = (list) => h('dl', { class: 'kv explain-kv' }, list.map(([k, v]) => [h('dt', null, k), h('dd', null, v)]));
    openFacts({
      title: factorLabel(key), value: `${info.weight}%`, valueNote: t('explain.ofScore', { layer: `${t('mode.access')} · ${t(`accl.p.${profile}`)}` }),
      sections: [
        h('section', { class: 'explain-sec' }, kv(rows)),
        info.dataCaveat ? h('div', { class: 'banner small' }, icon('info', 'sm'), h('span', null, info.dataCaveat)) : null,
        here ? h('section', { class: 'explain-sec' }, h('h3', null, t('explain.hereTitle')), kv(here)) : null
      ]
    });
  }).catch(unavailable);
}

// ── One square in the drawer ───────────────────────────────────────────────────────────────────────────────────
/** The accessibility part of the area drawer. c = the API's place; returns the nodes to put in the drawer's stack. */
export function accessCellSections(c, gm) {
  const L = c.access;
  const profile = c.accessProfile || L?.profile || getProfile();
  const has = L && L.hasData !== false && L.score !== null && L.score !== undefined;
  const open = () => openAccessScoreExplainer({ profile, place: c, meta: gm });
  const out = [];

  if (!has) {
    const code = L?.dataNoteCode || 'unavailable';
    out.push(h('div', { class: 'banner nodata-banner', role: 'status' }, icon('info'),
      h('div', { class: 'grow' }, h('b', null, t('pa.nodata.cell')), h('div', { class: 'small' }, t(`accl.noData.${['outside_area', 'no_data'].includes(code) ? code : 'unavailable'}`)),
        h('div', { class: 'small muted' }, t('pa.nodata.never')))));
    return out;
  }

  const box = h('div', { style: { textAlign: 'center' } }, gauge(L.score, gm, t('mode.access.score'), 'good'),
    h('div', { class: 'tiny muted' }, `${t('mode.access.score')} · ${t(`accl.p.${profile}`)}`), h('div', { class: 'tiny muted' }, bandText(bandOf(L.score, gm, 'good'), 'good')));
  out.push(h('div', { class: 'row wrap', style: { justifyContent: 'space-around' } }, explainable(box, open, t('mode.access.score'))));
  if (L.dataNoteCode === 'thin_coverage') out.push(h('p', { class: 'banner small' }, icon('alert', 'sm'), h('span', null, t('accl.noData.thin_coverage'))));

  // The same square for the other profiles: a score, or "no data" in words (never a good or a bad colour).
  const others = c.accessByProfile ? PROFILES.filter((p) => c.accessByProfile[p]) : [];
  if (others.length) {
    out.push(h('p', { class: 'small' }, h('b', null, `${t('accl.otherProfiles')}: `),
      others.map((p, i) => {
        const l = c.accessByProfile[p];
        const ok = l.hasData !== false && l.score !== null && l.score !== undefined;
        return [i ? ' · ' : '', `${t(`accl.p.${p}`)} `, ok ? h('span', { class: 'band', 'data-band': bandOf(l.score, gm, 'good') }, Math.round(l.score)) : h('span', { class: 'band nodata' }, t('accl.noData'))];
      })));
  }

  out.push(h('section', null, h('h3', null, t('cell.why')),
    explainable(h('p', { class: 'sect-title' }, `${t('mode.access.score')} · ${t('cell.base')} ${Math.round(L.baseScore)}${L.reportPenalty ? ` − ${L.reportPenalty} ${t('place.fromReports')}` : ''} = ${Math.round(L.score)} (${t('explain.dir.access')})`), open, t('mode.access.score')),
    h('div', { class: 'tbl-wrap' }, h('table', { class: 't' },
      h('thead', null, h('tr', null, [t('cell.factor'), t('cell.weight'), t('cell.value'), t('cell.score'), t('explain.col.access')].map((x) => h('th', { scope: 'col' }, x)))),
      h('tbody', null, L.factors.filter((f) => f.weight > 0).map((f) => {
        const openF = () => openAccessFactor(f.key, { factor: f, meta: gm });
        return h('tr', null,
          h('td', null, explainable(h('span', { class: 'row' }, icon(factorIcon(f.key), 'sm'), factorLabel(f.key)), openF, factorLabel(f.key))),
          h('td', { class: 'num' }, explainable(h('span', null, f.weight), openF, t('cell.weight'))),
          h('td', { class: 'num' }, explainable(h('span', null, accessValueText(f, gm)), openF, t('cell.value'))),
          h('td', null, explainable(f.hasData === false ? h('span', { class: 'band nodata' }, t('accl.noData')) : h('span', { class: 'band', 'data-band': bandOf(f.score, gm) }, Math.round(f.score)), openF, t('cell.score'))),
          h('td', { class: 'num' }, explainable(h('span', null, f.hasData === false ? '–' : `+${one(f.contribution)}`), openF, t('explain.col.access'))));
      })))),
    h('p', { class: 'tiny muted' }, t('pa.cellNote'))));
  return out;
}

/** Swatch for the "No data" legend row: grey with a dashed outline, so it is told apart by more than colour. */
export const noDataSwatch = () => h('span', { class: 'nodata-sw', 'aria-hidden': 'true' });
export { BAND_FILL };
