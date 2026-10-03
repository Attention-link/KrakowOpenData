// Planner reports: every citizen report with filters and the actions a planner needs (verify, resolve, show on map).

import { h, icon, clear, toast, timeAgo } from './util.js';
import { t, getLang } from './i18n.js';
import { P, pOn, loadReports, reportItem, staleBanner, openCellDrawer, requireOnline, dataChanged, openAlertDialog } from './planner-common.js';
import { seedDemo, errorText } from './api.js';

const TYPE_LAYER = { LightOut: 'Safety', UnsafeAtNight: 'Safety', PathHazard: 'Safety', WaterNotWorking: 'Heat', NoShade: 'Heat', HeatSpot: 'Heat' };

/** The report layer the planning event shows: Heat reports for Heat, night-safety reports for Night safety, all for Both. */
const eventLayer = () => (P.event === 'heat' ? 'Heat' : P.event === 'night' ? 'Safety' : null);

export function mount(host) {
  const cleanups = [];
  const f = { status: 'Open', type: '', verified: '', q: '' };
  let res = null;
  const page = h('div', { class: 'pl-page stack' });
  host.append(page);

  function matches(r) {
    if (eventLayer() && TYPE_LAYER[r.type] !== eventLayer()) return false;
    if (f.status && r.status !== f.status) return false;
    if (f.type && r.type !== f.type) return false;
    if (f.verified === 'yes' && !r.verifiedByPlanner) return false;
    if (f.verified === 'no' && r.verifiedByPlanner) return false;
    if (f.q && !`${r.note || ''} ${t(`rtype.${r.type}`)} ${r.cellId}`.toLowerCase().includes(f.q.toLowerCase())) return false;
    return true;
  }

  function render() {
    clear(page);
    page.append(h('div', { class: 'row between wrap' }, h('h1', null, t('rep.title')),
      h('button', { class: 'btn sm', type: 'button', onclick: seed }, icon('plus', 'sm'), t('rep.seed'))));
    if (!res) { page.append(h('div', { class: 'skeleton', style: { height: '240px' } })); return; }
    if (res.stale) page.append(staleBanner(res.savedAt));

    const types = ['LightOut', 'UnsafeAtNight', 'PathHazard', 'WaterNotWorking', 'NoShade', 'HeatSpot'];
    const q = h('input', { type: 'search', id: 'rep-q', value: f.q, placeholder: t('rep.search') });
    q.addEventListener('input', () => { f.q = q.value; paintList(); });
    // Every filter has a visible label above it.
    const select = (key, options) => h('label', { class: 'field filter' }, t(`rep.f.${key}`),
      h('select', { onchange: (e) => { f[key] = e.target.value; paintList(); } }, options.map(([v, l]) => h('option', { value: v, selected: f[key] === v }, l))));
    const wantedLayer = eventLayer();
    const shownTypes = wantedLayer ? types.filter((x) => TYPE_LAYER[x] === wantedLayer) : types;
    page.append(h('div', { class: 'card stack tight' },
      h('p', { class: 'small muted' }, icon('filter', 'sm'), ' ', t(wantedLayer ? `rep.showing.${P.event}` : 'rep.showing.both')),
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
    const items = res.data.filter(matches);
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
    try { res = await loadReports(); } catch (e) { res = { data: [], stale: false }; toast(errorText(e, t), { error: true }); }
    render();
  }

  cleanups.push(pOn('data', load), pOn('event', render));
  render();
  load();
  return () => cleanups.forEach((fn) => fn());
}
