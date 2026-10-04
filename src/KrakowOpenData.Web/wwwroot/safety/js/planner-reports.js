// Planner reports: every citizen report with filters and the actions a planner needs (verify, resolve, show on map).

import { h, icon, clear, toast, timeAgo } from './util.js';
import { t, getLang } from './i18n.js';
import { P, pOn, reportItem, staleBanner, openCellDrawer, requireOnline, dataChanged, openAlertDialog } from './planner-common.js';
import { seedDemo, errorText, cachedGet, getReports } from './api.js';
import { debounce } from './util.js';
import { REPORT_LAYER, LAYERS, modeOfEvent } from './model.js';

/** The report layer the planning event shows: heat reports for Heat, night-safety reports for Night safety, and so on. */
const eventLayer = () => modeOfEvent(P.event);

export function mount(host) {
  const cleanups = [];
  const f = { status: 'Open', type: '', verified: '', q: '' };
  let res = null;
  const page = h('div', { class: 'pl-page stack' });
  host.append(page);

  /**
   * The filters are applied by the API (layer of the planning event, status, type, verification, free text), so a filtered list is never
   * cut short by the 500-report limit. Free text also matches the type names in the language on screen, which only the app knows:
   * those types are asked for as well and the answers are merged.
   */
  async function fetchFiltered() {
    const base = { layer: LAYERS[eventLayer()].api, status: f.status || 'All', verified: f.verified === 'yes' ? true : f.verified === 'no' ? false : undefined, limit: 500 };
    const asks = [{ ...base, type: f.type || undefined, q: f.q || undefined }];
    const text = f.q.trim().toLowerCase();
    if (text) {
      for (const type of Object.keys(REPORT_LAYER)) {
        if (REPORT_LAYER[type] !== eventLayer() || (f.type && f.type !== type)) continue;
        if (t(`rtype.${type}`).toLowerCase().includes(text)) asks.push({ ...base, type });
      }
    }
    const answers = await Promise.all(asks.map((p) => cachedGet(`p:reports:${JSON.stringify(p)}`, () => getReports(p, true))));
    const seen = new Set();
    const data = answers.flatMap((a) => a.data).filter((r) => (seen.has(r.id) ? false : (seen.add(r.id), true)))
      .sort((a, b) => new Date(b.lastActivityAt) - new Date(a.lastActivityAt));
    return { data, stale: answers.some((a) => a.stale), savedAt: Math.min(...answers.map((a) => a.savedAt)) };
  }

  /** Fetches again after a filter changed and repaints only the list, so the search box keeps its focus. */
  let filterToken = 0;
  async function applyFilters() {
    const mine = ++filterToken;
    try { const next = await fetchFiltered(); if (mine !== filterToken) return; res = next; }
    catch (e) { if (mine !== filterToken) return; res = { data: [], stale: false }; toast(errorText(e, t), { error: true }); }
    paintList();
  }
  const applyFiltersSoon = debounce(applyFilters, 300);

  function render() {
    clear(page);
    page.append(h('div', { class: 'row between wrap' }, h('h1', null, t('rep.title')),
      h('button', { class: 'btn sm', type: 'button', onclick: seed }, icon('plus', 'sm'), t('rep.seed'))));
    if (!res) { page.append(h('div', { class: 'skeleton', style: { height: '240px' } })); return; }
    if (res.stale) page.append(staleBanner(res.savedAt));

    const types = Object.keys(REPORT_LAYER);
    const q = h('input', { type: 'search', id: 'rep-q', value: f.q, placeholder: t('rep.search') });
    q.addEventListener('input', () => { f.q = q.value; applyFiltersSoon(); });
    // Every filter has a visible label above it.
    const select = (key, options) => h('label', { class: 'field filter' }, t(`rep.f.${key}`),
      h('select', { onchange: (e) => { f[key] = e.target.value; applyFilters(); } }, options.map(([v, l]) => h('option', { value: v, selected: f[key] === v }, l))));
    const wantedLayer = eventLayer();
    const shownTypes = types.filter((x) => REPORT_LAYER[x] === wantedLayer);
    page.append(h('div', { class: 'card stack tight' },
      h('p', { class: 'small muted' }, icon('filter', 'sm'), ' ', t(`rep.showing.${P.event}`)),
      h('div', { class: 'row wrap', style: { alignItems: 'flex-end' } },
        select('status', [['Open', t('rep.open')], ['Resolved', t('rep.resolved')], ['', t('rep.all')]]),
        select('type', [['', t('rep.allTypes')], ...shownTypes.map((x) => [x, t(`rtype.${x}`)])]),
        select('verified', [['', t('rep.anyVerify')], ['yes', t('rep.verifiedOnly')], ['no', t('rep.unverifiedOnly')]]),
        h('label', { class: 'field filter grow', for: 'rep-q' }, t('rep.f.search'), q))));

    page.append(h('div', { class: 'card', id: 'rep-list' }));
    paintList();
    page.append(h('p', { class: 'tiny muted' }, t('rep.note')));
  }

  function paintList() {
    const box = page.querySelector('#rep-list');
    if (!box || !res) return;
    clear(box);
    const items = res.data;
    box.append(h('p', { class: 'small muted', 'aria-live': 'polite' }, t('rep.count', { n: items.length })));
    if (!items.length) { box.append(h('div', { class: 'empty' }, icon('flag'), h('p', null, t('rep.empty')))); return; }
    box.append(h('ul', { class: 'list' }, items.map((r) => {
      const li = reportItem(r, load);
      li.append(h('div', { class: 'row wrap', style: { marginTop: '.4rem' } },
        h('button', { class: 'btn sm quiet', type: 'button', onclick: () => { P.focus = { lat: r.latitude, lon: r.longitude, cellId: r.cellId }; location.hash = '#/planner/map'; } }, icon('map', 'sm'), t('rep.showMap')),
        h('button', { class: 'btn sm quiet', type: 'button', onclick: () => openCellDrawer(r.cellId) }, icon('target', 'sm'), t('rep.openArea')),
        r.status === 'Open' ? h('button', { class: 'btn sm quiet', type: 'button', onclick: () => openAlertDialog({ point: [r.latitude, r.longitude] }) }, icon('bell', 'sm'), t('cell.alert')) : null));
      return li;
    })));
  }

  async function seed() {
    if (!requireOnline()) return;
    try {
      const r = await seedDemo();
      toast(r.created ? t('rep.seeded', { n: r.created }) : t('rep.seedNone'));
      dataChanged();
    } catch (e) { toast(errorText(e, t), { error: true }); }
  }

  async function load() {
    filterToken++;   // a debounced filter request still in flight must not overwrite this newer result
    try { res = await fetchFiltered(); } catch (e) { res = { data: [], stale: false }; toast(errorText(e, t), { error: true }); }
    render();
  }

  // A new planning event is a different layer: the list must be fetched again, not just redrawn.
  cleanups.push(pOn('data', load), pOn('event', () => { f.type = ''; load(); }));
  render();
  load();
  return () => cleanups.forEach((fn) => fn());
}
