// Planner weights: how much each factor counts in each score, with the reason for the default weight beside every factor.
// A planner types any numbers; the server scales each layer to 100. A change applies to every score, residents' included.

import { h, icon, clear, toast } from './util.js';
import { t } from './i18n.js';
import { requireOnline, dataChanged, P } from './planner-common.js';
import { getWeights, setWeights, resetWeights, errorText } from './api.js';
import { resetMethod } from './explain.js';
import { FACTOR_ICON, LAYERS, layerOfApi, modeOfEvent } from './model.js';

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
    if (!data) { page.append(h('div', { class: 'skeleton', style: { height: '260px' } })); return; }

    if (data.customized) page.append(h('div', { class: 'banner small' }, icon('gear', 'sm'), h('span', null, t('wt.customized'))));

    const current = modeOfEvent(P.event);
    for (const layer of data.layers) page.append(layerCard(layer, layerOfApi(layer.layer) === current));

    page.append(h('div', { class: 'row wrap' },
      h('button', { class: 'btn primary', type: 'button', disabled: !anyDirty() || saving ? true : null, onclick: save }, icon('check', 'sm'), saving ? t('wt.saving') : t('wt.save')),
      h('button', { class: 'btn', type: 'button', disabled: !anyDirty() ? true : null, onclick: discard }, t('wt.discard')),
      h('div', { class: 'grow' }),
      h('button', { class: 'btn quiet', type: 'button', disabled: !data.customized || saving ? true : null, onclick: resetAll }, icon('refresh', 'sm'), t('wt.resetAll'))));
    page.append(h('p', { class: 'tiny muted' }, t('wt.note')));
  }

  function layerCard(layer, open) {
    const key = layerOfApi(layer.layer) || 'safety';
    const out = shares(layer);
    const total = layer.factors.reduce((a, f) => a + valueOf(f), 0);
    return h('details', { class: 'card flat wt-layer', open: open ? true : null },
      h('summary', { style: { cursor: 'pointer', fontWeight: 700 } }, icon(LAYERS[key].icon, 'sm'), ' ', layer.title,
        layer.customized ? h('span', { class: 'chip accent', style: { marginLeft: '.5rem' } }, t('wt.custom')) : null,
        dirtyLayer(layer) ? h('span', { class: 'chip warn', style: { marginLeft: '.5rem' } }, t('wt.unsaved')) : null),
      h('div', { class: 'stack', style: { marginTop: '.6rem' } },
        layer.factors.map((f, i) => factorRow(f, out[i], total)),
        h('div', { class: 'row between wrap small muted' },
          h('span', null, t('wt.total', { n: Math.round(total * 10) / 10 })),
          h('button', { class: 'btn sm quiet', type: 'button', onclick: () => { layer.factors.forEach((f) => { typed[f.key] = f.defaultWeight; }); render(); } }, t('wt.layerDefaults')))));
  }

  function factorRow(f, share, total) {
    const slider = h('input', { type: 'range', min: '0', max: '100', step: '1', value: String(valueOf(f)), 'aria-label': `${f.label}: ${t('wt.weight')}` });
    const number = h('input', { type: 'number', min: '0', max: '1000', step: '1', value: String(valueOf(f)), class: 'wt-num', 'aria-label': `${f.label}: ${t('wt.weight')}` });
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
        h('span', { class: 'row', style: { fontWeight: 600 } }, icon(FACTOR_ICON[f.key] || 'info', 'sm'), f.label),
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

  render();
  load();
  return () => {};
}
