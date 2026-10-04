// "What does this mean and why?" for every score and statistic. The numbers, weights, thresholds, reasons and data sources
// come from the API (/api/safety/method, built from the same code that computes the scores), so the explanation cannot drift
// from the model. Used by the resident cards and legend, and by the planner dashboard (every figure is clickable).

import { h, icon, clear, openDialog, formatDistance } from './util.js';
import { t } from './i18n.js';
import { cachedGet, getMethod, peek } from './api.js';
import { BAND_FILL, kindOf, bandOf, bandRange, FACTOR_ICON, LAYERS, layerOfApi } from './model.js';

// ── Method data (loaded once, saved for offline) ─────────────────────────────
let method = null;
let pending = null;

export const methodNow = () => method;

/** Forget the loaded method so the next explanation shows new weights (after a planner changed them). */
export function resetMethod() { method = null; pending = null; }

export function loadMethod() {
  if (method) return Promise.resolve(method);
  pending ||= cachedGet('method', getMethod).then((r) => { method = r.data; return method; }).finally(() => { pending = null; });
  return pending;
}

peek('method').then((s) => { if (s && !method) method = s.data; }).catch(() => {});

const layerOf = (key) => method?.layers.find((l) => l.layer === key) || null;
const factorInfo = (key) => method?.layers.flatMap((l) => l.factors).find((f) => f.key === key) || null;

/** The text of a band: heat has its own words (Low heat … Very high heat), the other scores Good … Critical. */
export const bandText = (band, kind = 'good') => t(kind === 'heat' ? `band.heat.${band}` : `band.${band}`);

/** One-line description of a score for a tooltip (the `title` attribute) and screen readers. */
export function scoreSummary(layer, score, band) {
  const kind = kindOf(layer);
  const dir = t(`explain.dir.${LAYERS[layer] ? layer : 'both'}`);
  return `${Math.round(score)} / 100 · ${bandText(band, kind)} · ${dir}`;
}

// ── Small building blocks ────────────────────────────────────────────────────
/** A round "!" button that opens an explanation. */
export function infoButton(onClick, label) {
  return h('button', { class: 'info-btn', type: 'button', title: t('explain.click'), 'aria-label': label || t('explain.how'), onclick: (e) => { e.stopPropagation(); onClick(); } }, '!');
}

/** Makes any element clickable (and keyboard operable) as an explanation trigger, with a hover hint. */
export function explainable(el, onOpen, hint) {
  el.classList.add('explainable');
  el.setAttribute('tabindex', '0');
  el.setAttribute('role', 'button');
  el.setAttribute('title', hint ? `${hint} · ${t('explain.click')}` : t('explain.click'));
  el.addEventListener('click', (e) => { e.stopPropagation(); onOpen(); });
  el.addEventListener('keydown', (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); e.stopPropagation(); onOpen(); } });
  return el;
}

const kv = (rows) => h('dl', { class: 'kv explain-kv' }, rows.filter(Boolean).map(([k, v]) => [h('dt', null, k), h('dd', null, v)]));

const section = (title, ...body) => h('section', { class: 'explain-sec' }, title ? h('h3', null, title) : null, ...body);

const sourceNote = (text) => h('p', { class: 'tiny muted' }, `${t('explain.source')}: ${text}`);

function bandTable(layer, meta) {
  const kind = kindOf(layer.layer);
  return h('ul', { class: 'list small band-list' }, layer.bands.map((b) => {
    const [from, to] = bandRange(b.band, meta, kind);
    return h('li', { class: 'row' },
      h('span', { class: 'band', 'data-band': b.band, style: { minWidth: '7.5rem', justifyContent: 'center' } }, `${Math.round(from)}–${Math.round(to)}`),
      h('span', null, h('b', null, bandText(b.band, kind)), ' · ', b.meaning));
  }));
}

function weightBar(weight) {
  return h('span', { class: 'wbar', role: 'img', 'aria-label': `${weight} / 100` }, h('span', { style: { width: `${weight}%` } }));
}

function valueText(f, radius) {
  if (f.unit === 'm') return f.value === null || f.value === undefined ? t('factor.none', { r: formatDistance(radius || 1500) }) : formatDistance(f.value);
  return `${Math.round(f.value ?? 0)} ${t('factor.lamps')}`;
}

/** What a measured factor did to the score, in words and numbers ("adds 25 points of heat"). */
function effectText(layerKey, f) {
  const pts = Math.round(f.contribution * 10) / 10;
  return t(layerKey === 'safety' ? 'explain.addsSafety' : `explain.adds.${layerKey}`, { n: pts, max: f.weight });
}

/** Name of a layer in words: "Night safety", "Heat", "Flood", "Air". */
const layerName = (key) => t(`mode.${LAYERS[key] ? key : 'safety'}`);

// ── The dialog ───────────────────────────────────────────────────────────────
/** Opens a dialog from {title, value, valueNote, band, intro, sections:[Node], footerLink}. */
export function openFacts({ title, value, valueNote, band, bandKind, intro, sections = [], extraFooter }) {
  return openDialog((close) => ({
    title,
    body: h('div', { class: 'stack explain' },
      value !== undefined && value !== null
        ? h('div', { class: 'explain-head' },
          h('span', { class: 'explain-value num' }, value),
          band ? h('span', { class: 'band', 'data-band': band }, bandText(band, bandKind)) : null,
          valueNote ? h('span', { class: 'small muted' }, valueNote) : null)
        : null,
      intro ? h('p', null, intro) : null,
      ...sections),
    footer: [extraFooter || null, h('button', { class: 'btn primary', type: 'button', onclick: () => close('ok') }, t('common.done'))]
  }));
}

function needMethod(open) {
  if (method) { open(); return; }
  loadMethod().then(open).catch(() => openFacts({ title: t('explain.title'), intro: t('explain.unavailable') }));
}

// ── Score explainer (what it means, the scale, why this place got it) ───────
/**
 * layer: 'heat' | 'safety' | 'both'. place: the API's PlaceScoreDto (optional). meta: grid thresholds (optional).
 * With a place the dialog also lists, factor by factor, what produced that number.
 */
export function openScoreExplainer({ layer, place = null, meta = null }) {
  needMethod(() => {
    const layers = layer === 'both' ? ['safety', 'heat'] : [layer];
    const sections = [];

    if (layer === 'both') {
      sections.push(section(t('explain.overallTitle'), h('p', null, method.combinedFormula)));
      if (place) sections.push(kv([[t('explain.overall'), `${Math.round(place.combined)} / 100 · ${bandText(place.combinedBand)}`]]));
    }

    for (const key of layers) {
      const L = layerOf(LAYERS[key].api);
      const data = place ? place[key] : null;
      const kind = kindOf(key);
      sections.push(section(L.title,
        h('p', { class: 'small' }, h('b', null, L.direction)),
        h('p', { class: 'small' }, L.meaning),
        data ? h('p', { class: 'explain-now' }, `${t('explain.thisPlace')}: `, h('b', null, `${Math.round(data.score)} / 100`), ' · ', bandText(data.band, kind)) : null,
        h('h4', null, t('explain.scale')), bandTable(L, meta),
        data ? whyPlace(key, L, data, meta) : null,
        h('h4', null, t('explain.formula')), h('p', { class: 'small' }, L.formula),
        key === 'heat' ? liveHeat() : null));
    }

    sections.push(section(t('explain.reportsTitle'), h('p', { class: 'small' }, method.reportRule)));
    sections.push(h('ul', { class: 'list tiny muted' }, method.limits.map((l) => h('li', null, l))));

    const first = layers[0];
    const d = place ? place[first] : null;
    openFacts({
      title: layer === 'both' ? t('explain.titleBoth') : layerOf(LAYERS[first].api).title,
      value: layer === 'both' ? (place ? Math.round(place.combined) : undefined) : (d ? Math.round(d.score) : undefined),
      valueNote: layer === 'both' || !d ? null : '/ 100',
      band: layer === 'both' ? place?.combinedBand : d?.band,
      bandKind: layer === 'both' ? 'good' : kindOf(first),
      sections,
      extraFooter: h('button', { class: 'btn', type: 'button', onclick: () => { document.querySelector('dialog[open]')?.close(); openMethod(meta); } }, icon('list', 'sm'), t('explain.fullMethod'))
    });
  });
}

function liveHeat() {
  if (method.temperatureC === null || method.temperatureC === undefined) return null;
  return h('p', { class: 'small' }, icon('thermo', 'sm'), ' ',
    t('explain.liveHeat', { c: method.temperatureC.toFixed(1), p: t(`heat.${method.heatPressure}`) }));
}

/** The factor table of one place: measured value, weight, what it added, and how many such features are mapped. */
function whyPlace(layerKey, L, data, meta) {
  const reports = data.reportPenalty;
  const total = Math.round(data.score);
  const rows = data.factors.map((f) => {
    const info = factorInfo(f.key);
    return h('tr', null,
      h('td', null, h('button', { class: 'linklike', type: 'button', onclick: () => openFactorExplainer(f.key, { factor: f, layer: layerKey, meta }) }, h('span', { class: 'row' }, icon(FACTOR_ICON[f.key] || 'info', 'sm'), t(`factor.${f.key}`)))),
      h('td', null, valueText(f, meta?.searchRadiusMeters), f.nearestName ? h('div', { class: 'tiny muted' }, f.nearestName) : null),
      h('td', { class: 'num' }, f.weight),
      h('td', { class: 'num' }, `+${Math.round(f.contribution * 10) / 10}`),
      h('td', { class: 'num muted' }, info ? info.mappedCount.toLocaleString() : '–'));
  });
  const top = [...data.factors].sort((a, b) => b.contribution - a.contribution).filter((f) => layerKey === 'heat' ? f.contribution > 0 : false).slice(0, 2);
  return h('div', null,
    h('h4', null, t('explain.whyThis')),
    layerKey === 'heat'
      ? h('p', { class: 'small' }, top.length ? t('explain.heatBiggest', { list: top.map((f) => `${t(`factor.${f.key}`)} (+${Math.round(f.contribution)})`).join(', ') }) : t('explain.heatNone'))
      : null,
    h('div', { class: 'tbl-wrap' }, h('table', { class: 't' },
      h('thead', null, h('tr', null, [t('cell.factor'), t('cell.value'), t('cell.weight'), layerKey === 'heat' ? t('explain.colHeat') : layerKey === 'safety' ? t('explain.colSafety') : t(`explain.col.${layerKey}`), t('explain.colMapped')].map((x) => h('th', { scope: 'col' }, x)))),
      h('tbody', null, rows,
        reports ? h('tr', null, h('td', { colspan: 3 }, layerKey === 'heat' || layerKey === 'safety' ? t('explain.reportsSubtract') : t('explain.reportsAndLive')), h('td', { class: 'num' }, `−${reports}`), h('td')) : null,
        h('tr', null, h('td', { colspan: 3 }, h('b', null, t('explain.total'))), h('td', { class: 'num' }, h('b', null, total)), h('td'))))),
    h('p', { class: 'tiny muted' }, layerKey === 'heat' ? t('explain.heatTableNote') : layerKey === 'safety' ? t('explain.safetyTableNote') : t(`explain.tableNote.${layerKey}`)));
}

// ── One factor ───────────────────────────────────────────────────────────────
export function openFactorExplainer(key, { factor = null, layer = null, meta = null } = {}) {
  needMethod(() => {
    const info = factorInfo(key);
    if (!info) { openFacts({ title: t(`factor.${key}`), intro: t('explain.unavailable') }); return; }
    const layerKey = layer || layerOfApi(info.layer) || 'safety';
    const rows = [
      [t('explain.measures'), info.measures],
      [t('explain.weight'), h('span', { class: 'row' }, h('b', null, `${info.weight} / 100`), weightBar(info.weight))],
      [t('explain.fullScore'), info.fullScoreAt],
      [t('explain.zeroScore'), info.zeroScoreAt],
      [t('explain.why'), info.whyWeighted],
      [t('explain.source'), info.source],
      [t('explain.mapped'), `${info.mappedCount.toLocaleString()}${key === 'lighting' ? ' ' + t('explain.lamps') : ''}`]
    ];
    const here = factor ? [
      [t('explain.hereValue'), valueText(factor, meta?.searchRadiusMeters)],
      factor.nearestName ? [t('explain.hereNearest'), factor.nearestName] : null,
      [t('explain.hereScore'), `${Math.round(factor.score)} / 100`],
      [t('explain.hereEffect'), effectText(layerKey, factor)]
    ] : null;
    openFacts({
      title: t(`factor.${key}`),
      value: `${info.weight}%`,
      valueNote: t('explain.ofScore', { layer: layerName(layerKey) }),
      sections: [
        section(null, kv(rows)),
        info.dataCaveat ? h('div', { class: 'banner small' }, icon('info', 'sm'), h('span', null, info.dataCaveat)) : null,
        here ? section(t('explain.hereTitle'), kv(here)) : null
      ]
    });
  });
}

// ── The whole method, with weights and sources ───────────────────────────────
export function openMethod(meta = null) {
  needMethod(() => {
    const sections = [h('p', null, t('explain.methodIntro'))];
    for (const L of method.layers) {
      sections.push(section(L.title,
        h('p', { class: 'small' }, h('b', null, L.direction)),
        h('p', { class: 'small' }, L.formula),
        h('h4', null, t('explain.weightsTitle')),
        ...L.factors.map((f) => h('div', { class: 'factor-doc' },
          h('div', { class: 'row between' }, h('b', { class: 'row' }, icon(FACTOR_ICON[f.key] || 'info', 'sm'), t(`factor.${f.key}`)), h('span', { class: 'row' }, h('b', null, `${f.weight}`), weightBar(f.weight))),
          h('p', { class: 'small' }, f.whyWeighted),
          h('p', { class: 'tiny muted' }, `${t('explain.fullScore')}: ${f.fullScoreAt} · ${t('explain.zeroScore')}: ${f.zeroScoreAt}`),
          h('p', { class: 'tiny muted' }, `${t('explain.source')}: ${f.source} · ${t('explain.mapped')}: ${f.mappedCount.toLocaleString()}`),
          f.dataCaveat ? h('p', { class: 'tiny' }, icon('info', 'sm'), ' ', f.dataCaveat) : null)),
        h('h4', null, t('explain.scale')), bandTable(L, meta)));
    }
    sections.push(section(t('explain.overallTitle'), h('p', { class: 'small' }, method.combinedFormula)));
    sections.push(section(t('explain.reportsTitle'), h('p', { class: 'small' }, method.reportRule)));
    sections.push(section(t('explain.priorityTitle'), h('p', { class: 'small' }, method.priorityFormula), h('p', { class: 'small' }, method.exposureFormula)));
    sections.push(h('ul', { class: 'list tiny muted' }, method.limits.map((l) => h('li', null, l))));
    openFacts({ title: t('explain.methodTitle'), sections });
  });
}

// ── Planner statistics ───────────────────────────────────────────────────────
/**
 * Explains one dashboard figure: what it is, how it is computed, where its data comes from, and the numbers behind it.
 * key: a KPI key from the method (cells, criticalCells, noWater500, …). extra: [[label, text]] with live numbers from the dashboard.
 */
export function openKpiExplainer(key, { value, unit, extra = [], links = [], title, factorKey } = {}) {
  needMethod(() => {
    const info = method.kpis.find((k) => k.key === key);
    const fi = factorKey ? factorInfo(factorKey) : null;
    if (fi) extra = [...extra, [t('explain.weight'), `${fi.weight} / 100 (${layerName(layerOfApi(fi.layer) || 'safety')})`], [t('explain.mapped'), fi.mappedCount.toLocaleString()], [t('explain.why'), fi.whyWeighted]];
    const shown = value === undefined || value === null ? undefined : unit === '%' ? `${Math.round(value)}%` : unit === 'score' ? `${Math.round(value)} / 100` : String(value);
    openFacts({
      title: title || info?.title || t(`kpi.${key}`),
      value: shown,
      intro: info?.definition || t('explain.unavailable'),
      sections: [
        info ? section(t('explain.howComputed'), h('p', { class: 'small' }, info.formula), sourceNote(info.source)) : null,
        key === 'priority' ? section(t('explain.priorityTitle'), h('p', { class: 'small' }, method.exposureFormula)) : null,
        extra.length ? section(t('explain.numbersBehind'), kv(extra)) : null,
        links.length ? h('div', { class: 'row wrap' }, links.map((l) => h('a', { class: 'btn sm', href: l.href, onclick: () => document.querySelector('dialog[open]')?.close() }, l.label))) : null
      ]
    });
  });
}

// ── Map legend content (shared by the resident and planner maps) ─────────────
/**
 * Legend body for a view: for each score shown, its direction ("higher = hotter"), the coloured ranges with names and a
 * button to the full explanation. mode: 'heat' | 'safety' | 'both'.
 */
export function legendBody(mode, meta, { onExplain, compact = false } = {}) {
  const parts = [];
  const rows = (kind, title, sub) => {
    parts.push(h('div', { class: 'legend-block' },
      h('div', { class: 'row between' }, h('b', { class: 'small' }, title), infoButton(() => onExplain?.(LAYERS[mode] ? mode : 'both'), t('explain.how'))),
      h('div', { class: 'tiny muted' }, sub),
      h('ul', { class: 'legend-rows' }, ['Good', 'Fair', 'Weak', 'Critical'].map((b) => {
        const [from, to] = bandRange(b, meta, kind);
        const meaning = layerMeaning(LAYERS[mode] ? LAYERS[mode].api : null, b) || t(kind === 'heat' ? `band.heat.${b}.desc` : `band.${b}.desc`);
        return h('li', null, h('i', { style: { background: BAND_FILL[b] } }), h('span', { class: 'num' }, `${Math.round(from)}–${Math.round(to)}`), h('span', null, h('b', null, bandText(b, kind)), compact ? null : ' · ', compact ? null : meaning));
      }))));
  };
  if (mode === 'heat') rows('heat', t('legend.heatTitle'), t('explain.dir.heat'));
  else if (LAYERS[mode]) rows('good', t(`legend.${mode}Title`), t(`explain.dir.${mode}`));
  else {
    rows('good', t('legend.bothTitle'), t('explain.dir.both'));
    parts.push(h('div', { class: 'tiny muted' }, t('legend.bothNote')));
  }
  return parts;
}

function layerMeaning(layerName, band) {
  if (!method || !layerName) return null;
  return layerOf(layerName)?.bands.find((b) => b.band === band)?.meaning || null;
}

export { bandOf };
