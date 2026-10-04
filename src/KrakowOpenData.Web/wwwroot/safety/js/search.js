// Address / stop search box with autocomplete and visible filters (All, Addresses, Stops).
// Addresses come from OpenStreetMap through the API; stops from the timetable data. Needs a connection.

import { h, icon, clear, debounce } from './util.js';
import { isOffline, on } from './state.js';
import { t } from './i18n.js';
import { geoSearch, searchStops } from './api.js';

let counter = 0;

/**
 * searchBox({label, placeholder, near, onPick, filters})
 *   onPick({label, lat, lon, kind: 'address' | 'stop'}) is called when the user picks a result.
 * Returns an element with setText(text) and focus().
 */
export function searchBox({ label, placeholder, near = null, onPick, filters = true, initial = '' } = {}) {
  const id = `sb-${++counter}`;
  let filter = 'all';
  let results = [];
  let active = -1;
  let token = 0;

  const input = h('input', {
    type: 'search', id, autocomplete: 'off', placeholder, role: 'combobox', 'aria-expanded': 'false', 'aria-controls': `${id}-list`,
    'aria-autocomplete': 'list', enterkeyhint: 'search', spellcheck: 'false'
  });
  input.value = initial;
  const list = h('ul', { class: 'sb-list', id: `${id}-list`, role: 'listbox', hidden: true, 'aria-label': t('search.results') });
  const status = h('p', { class: 'tiny muted', 'aria-live': 'polite' });
  const filterBar = h('div', { class: 'row wrap sb-filters', role: 'group', 'aria-label': t('search.filter') });

  const FILTERS = [['all', 'search.all'], ['address', 'search.addresses'], ['stop', 'search.stops']];
  function paintFilters() {
    clear(filterBar);
    filterBar.append(h('span', { class: 'tiny muted' }, t('search.show')),
      ...FILTERS.map(([k, key]) => h('button', { type: 'button', class: `chip-btn${filter === k ? ' on' : ''}`, 'aria-pressed': String(filter === k),
        onclick: () => { filter = k; paintFilters(); paintList(); if (results.length) status.textContent = visible().length ? '' : t('search.none'); } }, t(key))));
  }
  if (filters) paintFilters();

  function visible() { return results.filter((r) => filter === 'all' || r.kind === filter); }

  function paintList() {
    clear(list);
    const shown = visible();
    active = -1;
    input.setAttribute('aria-expanded', String(shown.length > 0));
    list.hidden = shown.length === 0;
    shown.forEach((r, i) => {
      list.append(h('li', { role: 'option', id: `${id}-o${i}`, class: 'sb-item', onmousedown: (e) => { e.preventDefault(); pick(r); } },
        icon(r.kind === 'stop' ? 'bus' : 'pin', 'sm'),
        h('span', { class: 'grow' }, h('span', { class: 'sb-title' }, r.label), r.detail ? h('span', { class: 'tiny muted', style: { display: 'block' } }, r.detail) : null),
        h('span', { class: 'chip' }, t(r.kind === 'stop' ? 'search.stop' : 'search.address'))));
    });
  }

  function pick(r) {
    input.value = r.label;
    results = [];
    paintList();
    status.textContent = '';
    onPick?.(r);
  }

  // 300 ms after the last keystroke, one search; a newer one cancels the request still running, so a busy server
  // (an event with hundreds of phones) only works on what the user is typing now.
  let inflight = null;
  const run = debounce(async () => {
    const text = input.value.trim();
    const mine = ++token;
    inflight?.abort();
    inflight = null;
    if (text.length < 2) { results = []; paintList(); status.textContent = ''; return; }
    status.textContent = t('search.searching');
    const ctrl = new AbortController();
    inflight = ctrl;
    const [addr, stops] = await Promise.allSettled([geoSearch(text, near?.(), ctrl.signal), searchStops(text, ctrl.signal)]);
    if (inflight === ctrl) inflight = null;
    if (mine !== token) return;
    results = [];
    if (addr.status === 'fulfilled') results.push(...addr.value.map((a) => ({ kind: 'address', label: a.label, lat: a.latitude, lon: a.longitude, detail: a.postcode ? `${a.postcode}` : null })));
    if (stops.status === 'fulfilled') results.push(...(stops.value.items || []).map((s) => ({ kind: 'stop', label: s.name + (s.code ? ` ${s.code}` : ''), lat: s.latitude, lon: s.longitude, detail: t('search.stopDetail') })));
    // The same stop appears once per timetable feed: keep one.
    const seen = new Set();
    results = results.filter((r) => { const k = `${r.kind}|${r.label}|${r.lat.toFixed(3)}|${r.lon.toFixed(3)}`; return seen.has(k) ? false : (seen.add(k), true); });
    const failed = addr.status === 'rejected' && stops.status === 'rejected';
    status.textContent = failed ? t('search.error') : visible().length ? '' : t('search.none');
    paintList();
  }, 300);

  input.addEventListener('input', run);
  input.addEventListener('focus', () => { if (results.length) paintList(); });
  input.addEventListener('blur', () => setTimeout(() => { list.hidden = true; input.setAttribute('aria-expanded', 'false'); }, 120));
  input.addEventListener('keydown', (e) => {
    const shown = visible();
    if (e.key === 'ArrowDown' && shown.length) { e.preventDefault(); list.hidden = false; active = (active + 1) % shown.length; mark(); }
    else if (e.key === 'ArrowUp' && shown.length) { e.preventDefault(); active = (active - 1 + shown.length) % shown.length; mark(); }
    else if (e.key === 'Enter') { if (active >= 0 && shown[active]) { e.preventDefault(); pick(shown[active]); } else if (shown[0]) { e.preventDefault(); pick(shown[0]); } }
    else if (e.key === 'Escape') { list.hidden = true; }
  });
  function mark() {
    [...list.children].forEach((li, i) => li.setAttribute('aria-selected', String(i === active)));
    input.setAttribute('aria-activedescendant', active >= 0 ? `${id}-o${active}` : '');
    list.children[active]?.scrollIntoView({ block: 'nearest' });
  }

  const hint = h('p', { class: 'tiny muted' }, t('search.offline'));
  const el = h('div', { class: 'field sb' },
    label ? h('label', { for: id }, label) : null,
    h('div', { class: 'sb-wrap' }, input, list),
    filters ? filterBar : null, status, hint);

  function paintOffline() {
    const off = isOffline();
    input.disabled = off;
    hint.hidden = !off;
  }
  paintOffline();
  const offs = [on('online', paintOffline), on('apiOk', paintOffline)];
  el.setText = (text) => { input.value = text; };
  el.focusInput = () => input.focus();
  el.destroy = () => offs.forEach((off) => off());
  return el;
}
