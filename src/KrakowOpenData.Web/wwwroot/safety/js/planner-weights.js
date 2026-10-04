// Planner weights: how much each factor counts in each score, with the reason for the default weight beside every factor.
// A planner types any numbers; the server scales each layer to 100. A change applies to every score, residents' included.

import { h, icon, clear, toast } from './util.js';
import { t } from './i18n.js';
import { requireOnline, dataChanged, P } from './planner-common.js';
import { getWeights, setWeights, resetWeights, getRouteThresholds, setRouteThresholds, resetRouteThresholds, errorText } from './api.js';
import { resetMethod } from './explain.js';
import { FACTOR_ICON, LAYERS, layerOfApi } from './model.js';
import { layerMode, PROFILE_API, factorLabel, accessIcon } from './planner-access.js';

/** The accessibility layers are three (one per profile): which profile an API layer name stands for, and how the layer is titled and drawn. */
const profileOfLayer = (name) => Object.keys(PROFILE_API).find((p) => PROFILE_API[p] === name) || null;
const layerTitle = (l) => (profileOfLayer(l.layer) ? `${t('mode.access.score')} · ${t(`accl.p.${profileOfLayer(l.layer)}`)}` : l.title);
const thrTitle = (l) => (profileOfLayer(l.layer) ? t(`pa.thr.title.${profileOfLayer(l.layer)}`) : l.title);
const layerIcon = (key) => (key === 'access' ? accessIcon() : LAYERS[key].icon);
/** The card of the layer being planned opens; for accessibility only the card of the chosen profile. */
const isCurrent = (l) => { const k = layerOfApi(l.layer); return k === layerMode(P.event) && (k !== 'access' || l.layer === PROFILE_API[P.profile]); };

export function mount(host) {
  let data = null;               // the API's WeightsDto
  const typed = {};              // factor key -> number typed by the planner (only while it differs from the saved weight)
  let saving = false;
  const page = h('div', { class: 'pl-page stack' });
  host.append(page);

  const savedWeight = (f) => f.weight;
  const valueOf = (f) => (typed[f.key] !== undefined ? typed[f.key] : savedWeight(f));
  const dirtyLayer = (layer) => layer.factors.some((f) => typed[f.key] !== undefined && typed[f.key] !== savedWeight(f));
  const anyDirty = () => data?.layers.some(dirtyLayer);

  /** What the typed numbers become: each layer scaled to 100 (the server does the same, with whole points). */
  function shares(layer) {
    const total = layer.factors.reduce((a, f) => a + valueOf(f), 0);
    return layer.factors.map((f) => (total > 0 ? Math.round((valueOf(f) * 100) / total) : 0));
  }

  function render() {
    clear(page);
    page.append(h('h1', null, t('wt.title')),
      h('div', { class: 'banner info small' }, icon('info', 'sm'), h('span', null, t('wt.intro'))),
      h('div', { class: 'banner small' }, icon('alert', 'sm'), h('span', null, t('wt.shared'))));
    if (!data) { page.append(h('div', { class: 'skeleton', style: { height: '260px' } }), thrHost); return; }

    if (data.customized) page.append(h('div', { class: 'banner small' }, icon('gear', 'sm'), h('span', null, t('wt.customized'))));

    for (const layer of data.layers) page.append(layerCard(layer, isCurrent(layer)));

    page.append(h('div', { class: 'row wrap' },
      h('button', { class: 'btn primary', type: 'button', disabled: !anyDirty() || saving ? true : null, onclick: save }, icon('check', 'sm'), saving ? t('wt.saving') : t('wt.save')),
      h('button', { class: 'btn', type: 'button', disabled: !anyDirty() ? true : null, onclick: discard }, t('wt.discard')),
      h('div', { class: 'grow' }),
      h('button', { class: 'btn quiet', type: 'button', disabled: !data.customized || saving ? true : null, onclick: resetAll }, icon('refresh', 'sm'), t('wt.resetAll'))));
    page.append(h('p', { class: 'tiny muted' }, t('wt.note')), thrHost);
  }

  function layerCard(layer, open) {
    const key = layerOfApi(layer.layer) || 'safety';
    const out = shares(layer);
    const total = layer.factors.reduce((a, f) => a + valueOf(f), 0);
    return h('details', { class: 'card flat wt-layer', open: open ? true : null },
      h('summary', { style: { cursor: 'pointer', fontWeight: 700 } }, icon(layerIcon(key), 'sm'), ' ', layerTitle(layer),
        layer.customized ? h('span', { class: 'chip accent', style: { marginLeft: '.5rem' } }, t('wt.custom')) : null,
        dirtyLayer(layer) ? h('span', { class: 'chip warn', style: { marginLeft: '.5rem' } }, t('wt.unsaved')) : null),
      h('div', { class: 'stack', style: { marginTop: '.6rem' } },
        layer.factors.map((f, i) => factorRow(f, out[i], total)),
        h('div', { class: 'row between wrap small muted' },
          h('span', null, t('wt.total', { n: Math.round(total * 10) / 10 })),
          h('button', { class: 'btn sm quiet', type: 'button', onclick: () => { layer.factors.forEach((f) => { typed[f.key] = f.defaultWeight; }); render(); } }, t('wt.layerDefaults')))));
  }

  const labelOf = (f) => (f.key.startsWith('access.') ? factorLabel(f.key) : f.label);

  function factorRow(f, share, total) {
    const slider = h('input', { type: 'range', min: '0', max: '100', step: '1', value: String(valueOf(f)), 'aria-label': `${labelOf(f)}: ${t('wt.weight')}` });
    const number = h('input', { type: 'number', min: '0', max: '1000', step: '1', value: String(valueOf(f)), class: 'wt-num', 'aria-label': `${labelOf(f)}: ${t('wt.weight')}` });
    const out = h('b', { class: 'num' }, `${share}`);
    const set = (v) => {
      const n = Math.max(0, Math.min(1000, Number.isFinite(v) ? v : 0));
      if (n === savedWeight(f)) delete typed[f.key]; else typed[f.key] = n;
      render();
    };
    slider.addEventListener('input', () => set(Number(slider.value)));
    number.addEventListener('change', () => set(Number(number.value)));

    return h('div', { class: 'wt-row' },
      h('div', { class: 'row between wrap' },
        h('span', { class: 'row', style: { fontWeight: 600 } }, icon(FACTOR_ICON[f.key] || 'info', 'sm'), labelOf(f)),
        h('span', { class: 'row small' }, t('wt.share'), ' ', out, ' / 100', share !== f.defaultWeight ? h('span', { class: 'tiny muted' }, ` (${t('wt.default', { n: f.defaultWeight })})`) : h('span', { class: 'tiny muted' }, ` (${t('wt.isDefault')})`))),
      h('div', { class: 'row wt-controls' }, slider, number),
      h('p', { class: 'small' }, h('b', null, t('wt.why')), ' ', f.why),
      h('details', { class: 'tiny' }, h('summary', { style: { cursor: 'pointer' } }, t('wt.more')),
        h('p', null, h('b', null, t('wt.measures')), ' ', f.measures),
        h('p', null, h('b', null, t('explain.source')), ': ', f.source),
        f.caveat ? h('p', null, icon('info', 'sm'), ' ', f.caveat) : null));
  }

  async function save() {
    if (!requireOnline() || saving) return;
    const body = {};
    for (const layer of data.layers) if (dirtyLayer(layer)) layer.factors.forEach((f) => { body[f.key] = valueOf(f); });
    saving = true; render();
    try {
      data = await setWeights(body);
      Object.keys(typed).forEach((k) => delete typed[k]);
      resetMethod();
      toast(t('wt.saved'));
      dataChanged();
    } catch (e) { toast(errorText(e, t), { error: true }); }
    saving = false; render();
  }

  async function resetAll() {
    if (!requireOnline() || saving) return;
    if (!confirm(t('wt.resetConfirm'))) return;
    saving = true; render();
    try {
      data = await resetWeights();
      Object.keys(typed).forEach((k) => delete typed[k]);
      resetMethod();
      toast(t('wt.resetDone'));
      dataChanged();
    } catch (e) { toast(errorText(e, t), { error: true }); }
    saving = false; render();
  }

  function discard() { Object.keys(typed).forEach((k) => delete typed[k]); render(); }

  async function load() {
    try { data = await getWeights(); } catch (e) { toast(errorText(e, t), { error: true }); data = null; }
    render();
  }

  // ── Route thresholds: when is the fastest walking route "good enough"? (separate save/reset from the weights above) ──
  let thr = null;                // the API's RouteThresholdsDto
  const tTyped = {};             // layer name -> { average, worst } typed but not saved
  let thrSaving = false;
  const thrHost = h('div', { class: 'stack' });

  const thrValue = (l) => tTyped[l.layer] || { average: l.average, worst: l.worst };
  const thrDirty = (l) => !!tTyped[l.layer] && (tTyped[l.layer].average !== l.average || tTyped[l.layer].worst !== l.worst);
  function thrError(l) {
    const v = thrValue(l);
    const bad = (n) => !Number.isFinite(n) || n < thr.min || n > thr.max;
    if (bad(v.average) || bad(v.worst)) return t('th.errRange', { min: thr.min, max: thr.max });
    if (v.worst > v.average) return t('th.errOrder');
    return null;
  }
  const thrAnyDirty = () => !!thr && thr.layers.some(thrDirty);
  const thrAnyError = () => !!thr && thr.layers.some((l) => thrDirty(l) && thrError(l));

  function renderThresholds() {
    clear(thrHost);
    thrHost.append(h('h2', null, t('th.title')), h('div', { class: 'banner info small' }, icon('info', 'sm'), h('span', null, t('th.intro'))));
    if (!thr) { thrHost.append(h('div', { class: 'skeleton', style: { height: '160px' } })); return; }
    for (const l of thr.layers) thrHost.append(thrCard(l, isCurrent(l)));
    thrHost.append(h('div', { class: 'row wrap' },
      h('button', { class: 'btn primary', type: 'button', disabled: !thrAnyDirty() || thrAnyError() || thrSaving ? true : null, onclick: saveThresholds }, icon('check', 'sm'), thrSaving ? t('wt.saving') : t('th.save')),
      h('button', { class: 'btn', type: 'button', disabled: !thrAnyDirty() ? true : null, onclick: () => { Object.keys(tTyped).forEach((k) => delete tTyped[k]); renderThresholds(); } }, t('wt.discard')),
      h('div', { class: 'grow' }),
      h('button', { class: 'btn quiet', type: 'button', disabled: !thr.customized || thrSaving ? true : null, onclick: resetThresholds }, icon('refresh', 'sm'), t('th.resetAll'))));
  }

  function thrCard(l, open) {
    const key = layerOfApi(l.layer) || 'safety';
    const v = thrValue(l);
    const err = thrError(l);
    const field = (which, label) => {
      const id = `th-${l.layer}-${which}`;
      const input = h('input', { type: 'number', min: String(thr.min), max: String(thr.max), step: '1', id, class: 'wt-num', value: String(v[which]),
        'aria-describedby': err ? `${id}-err` : null, 'aria-invalid': err ? 'true' : null });
      input.addEventListener('change', () => {
        const next = { ...thrValue(l), [which]: Number(input.value) };
        if (next.average === l.average && next.worst === l.worst) delete tTyped[l.layer]; else tTyped[l.layer] = next;
        renderThresholds();
      });
      return h('label', { class: 'th-field', for: id }, h('span', { class: 'small' }, label), input);
    };
    return h('details', { class: 'card flat wt-layer', open: open ? true : null },
      h('summary', { style: { cursor: 'pointer', fontWeight: 700 } }, icon(layerIcon(key), 'sm'), ' ', thrTitle(l),
        l.customized ? h('span', { class: 'chip accent', style: { marginLeft: '.5rem' } }, t('wt.custom')) : null,
        thrDirty(l) ? h('span', { class: 'chip warn', style: { marginLeft: '.5rem' } }, t('wt.unsaved')) : null),
      h('div', { class: 'stack', style: { marginTop: '.6rem' } },
        h('div', { class: 'row wrap th-fields' }, field('average', t('th.average')), field('worst', t('th.worst')),
          h('span', { class: 'tiny muted' }, t('th.defaultIs', { avg: l.defaultAverage, worst: l.defaultWorst }))),
        err ? h('p', { class: 'err', id: `th-${l.layer}-average-err`, role: 'alert' }, err) : null,
        h('p', { class: 'small' }, h('b', null, t('th.why')), ' ', t(`th.why.${profileOfLayer(l.layer) ? `access.${profileOfLayer(l.layer)}` : key}`)),
        h('div', { class: 'row between wrap small muted' }, h('span', null),
          h('button', { class: 'btn sm quiet', type: 'button', onclick: () => { tTyped[l.layer] = { average: l.defaultAverage, worst: l.defaultWorst }; if (l.average === l.defaultAverage && l.worst === l.defaultWorst) delete tTyped[l.layer]; renderThresholds(); } }, t('th.layerDefaults')))));
  }

  async function saveThresholds() {
    if (!requireOnline() || thrSaving || thrAnyError()) return;
    const body = {};
    for (const l of thr.layers) if (thrDirty(l)) body[l.layer] = { average: tTyped[l.layer].average, worst: tTyped[l.layer].worst };
    thrSaving = true; renderThresholds();
    try {
      thr = await setRouteThresholds(body);
      Object.keys(tTyped).forEach((k) => delete tTyped[k]);
      toast(t('th.saved'));
    } catch (e) { toast(errorText(e, t), { error: true }); }
    thrSaving = false; renderThresholds();
  }

  async function resetThresholds() {
    if (!requireOnline() || thrSaving) return;
    if (!confirm(t('th.resetConfirm'))) return;
    thrSaving = true; renderThresholds();
    try {
      thr = await resetRouteThresholds();
      Object.keys(tTyped).forEach((k) => delete tTyped[k]);
      toast(t('th.resetDone'));
    } catch (e) { toast(errorText(e, t), { error: true }); }
    thrSaving = false; renderThresholds();
  }

  async function loadThresholds() {
    try { thr = await getRouteThresholds(); } catch (e) { thr = null; }
    renderThresholds();
  }

  render();
  load();
  renderThresholds();
  loadThresholds();
  return () => {};
}
