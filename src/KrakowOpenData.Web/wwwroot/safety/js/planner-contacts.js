// Planner contacts: the agencies a planner can reach, a prefilled brief for each, and the log of contacts made.

import { h, icon, clear, toast, timeAgo } from './util.js';
import { t, getLang } from './i18n.js';
import { pOn, loadAgencies, loadDispatches, openDispatchComposer, staleBanner } from './planner-common.js';
import { errorText } from './api.js';

export function mount(host) {
  const cleanups = [];
  let agencies = null, dispatches = null;
  const page = h('div', { class: 'pl-page stack' });
  host.append(page);

  function render() {
    clear(page);
    page.append(h('h1', null, t('ag.page')), h('div', { class: 'banner info small' }, icon('info', 'sm'), h('span', null, t('ag.simulated'))));
    if (agencies?.stale) page.append(staleBanner(agencies.savedAt));

    page.append(h('div', { class: 'grid-2' }, (agencies?.data || []).map((a) => h('section', { class: 'card' },
      h('h2', null, a.name),
      h('p', { class: 'small muted' }, a.responsibility),
      h('div', { class: 'row wrap', style: { margin: '.6rem 0' } },
        a.phone ? h('a', { class: 'btn sm', href: `tel:${a.phone.replace(/\s/g, '')}` }, icon('phone', 'sm'), a.phone) : null,
        a.phone && !a.contactVerified ? h('span', { class: 'chip warn', title: t('ag.verifyHelp') }, icon('alert', 'sm'), t('ag.verify')) : null,
        a.url ? h('a', { class: 'btn sm', href: a.url, target: '_blank', rel: 'noopener' }, icon('external', 'sm'), t('ag.website')) : null),
      h('button', { class: 'btn primary sm', type: 'button', onclick: () => openDispatchComposer({ agencyId: a.id }) }, icon('send', 'sm'), t('ag.newContact'))))));
    if (!agencies) page.append(h('div', { class: 'skeleton', style: { height: '160px' } }));

    const log = h('section', { class: 'card' }, h('h2', null, t('ag.log')));
    if (!dispatches) log.append(h('div', { class: 'skeleton', style: { height: '80px' } }));
    else if (!dispatches.data.length) log.append(h('p', { class: 'small muted' }, t('ag.logEmpty')));
    else log.append(h('div', { class: 'tbl-wrap' }, h('table', { class: 't' },
      h('thead', null, h('tr', null, [t('ag.when'), t('ag.agency'), t('ag.subject'), t('ag.reference'), t('ag.delivery')].map((x) => h('th', { scope: 'col' }, x)))),
      h('tbody', null, dispatches.data.map((d) => h('tr', null,
        h('td', null, timeAgo(d.createdAt, t, getLang())), h('td', null, d.agencyName), h('td', null, d.subject),
        h('td', { class: 'num' }, d.reference), h('td', null, h('span', { class: 'chip warn' }, t(`ag.delivery.${d.delivery}`)))))))));
    page.append(log);
  }

  async function load() {
    try { agencies = await loadAgencies(); } catch (e) { agencies = { data: [] }; toast(errorText(e, t), { error: true }); }
    try { dispatches = await loadDispatches(); } catch { dispatches = { data: [] }; }
    render();
  }
  cleanups.push(pOn('data', load));
  render();
  load();
  return () => cleanups.forEach((fn) => fn());
}
