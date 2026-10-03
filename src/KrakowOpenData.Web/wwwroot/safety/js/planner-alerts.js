// Planner alerts: compose a message for everyone inside a circle, see how many phones it will reach, send it,
// and manage the alerts that are running. Residents' apps pick alerts up when they next check their area.

import { h, icon, clear, toast, timeLeft, timeAgo, debounce } from './util.js';
import { t, tIn, getLang } from './i18n.js';
import { P, pOn, loadAlerts, staleBanner, requireOnline, dataChanged } from './planner-common.js';
import { createAlert, cancelAlert, getReach, errorText } from './api.js';
import { createMap, watchResize, KRAKOW } from './map.js';
import { searchBox } from './search.js';
import { addressLine } from './geo.js';

const SEV = { Info: 'accent', Warning: 'warn', Critical: 'danger' };
const TEMPLATES = ['heat', 'night', 'custom'];

/** The alert form. Used on the Alerts page and in the dialog opened from a map area or a report. */
export function alertForm({ cell, action, point, onSent } = {}) {
  const cleanups = [];
  const start = cell ? [cell.latitude, cell.longitude] : point || KRAKOW;
  const f = {
    center: start, radius: 800,
    // Start from the template that fits: the action's layer, else the weaker layer of the area, else the planning event.
    template: action?.layer === 'Heat' ? 'heat' : action?.layer === 'Safety' ? 'night'
      : cell ? (100 - cell.heat.score < cell.safety.score ? 'heat' : 'night')
        : P.event === 'heat' ? 'heat' : P.event === 'night' ? 'night' : 'custom',
    severity: 'Warning', layer: '', duration: 180, title: '', message: '', pl: '', uk: '', armed: false
  };

  const el = h('form', { class: 'stack', novalidate: true });
  const mapEl = h('div', { class: 'map', style: { position: 'relative', height: '220px', borderRadius: '10px', zIndex: 0 }, role: 'application', 'aria-label': t('al.mapLabel') });
  const reachEl = h('p', { class: 'small', 'aria-live': 'polite' });
  const preview = h('div', { class: 'preview' });
  const errorEl = h('p', { class: 'err', role: 'alert', hidden: true });

  const title = h('input', { type: 'text', maxlength: '80', required: true, id: 'al-title' });
  const message = h('textarea', { rows: '3', maxlength: '500', required: true, id: 'al-msg' });
  const pl = h('textarea', { rows: '2', maxlength: '500', placeholder: t('al.optional') });
  const uk = h('textarea', { rows: '2', maxlength: '500', placeholder: t('al.optional') });
  const radius = h('input', { type: 'range', min: '100', max: '3000', step: '100', value: String(f.radius), 'aria-label': t('al.radius') });
  const radiusLabel = h('b', null);
  const sendBtn = h('button', { class: 'btn primary', type: 'submit' }, icon('send', 'sm'), t('al.send'));

  const applyTemplate = (id) => {
    f.template = id;
    if (id === 'custom') return;
    const key = id === 'heat' ? 'al.tpl.heat' : 'al.tpl.night';
    title.value = tIn('en', `${key}.title`); message.value = tIn('en', `${key}.body`);
    pl.value = tIn('pl', `${key}.body`); uk.value = tIn('uk', `${key}.body`);
    f.severity = id === 'heat' ? 'Warning' : 'Info';
    f.layer = id === 'heat' ? 'Heat' : 'Safety';
    sevSel.value = f.severity; layerSel.value = f.layer;
    // Title in the resident's language is the message's first words; the title stays English/Polish for planners.
    title.value = tIn(getLang() === 'pl' ? 'pl' : 'en', `${key}.title`);
    paintPreview();
  };

  const tplSel = h('select', { id: 'al-tpl' }, TEMPLATES.map((id) => h('option', { value: id, selected: f.template === id }, t(`al.tpl.${id}`))));
  tplSel.addEventListener('change', () => applyTemplate(tplSel.value));
  const sevSel = h('select', { id: 'al-sev' }, ['Info', 'Warning', 'Critical'].map((s) => h('option', { value: s, selected: f.severity === s }, t(`sev.${s}`))));
  const layerSel = h('select', { id: 'al-layer' }, [['', t('al.layerAny')], ['Heat', t('mode.heat')], ['Safety', t('mode.safety')]].map(([v, l]) => h('option', { value: v, selected: f.layer === v }, l)));
  const durSel = h('select', { id: 'al-dur' }, [[30, '30 min'], [60, '1 h'], [180, '3 h'], [360, '6 h'], [720, '12 h'], [1440, '24 h']].map(([v, l]) => h('option', { value: v, selected: f.duration === v }, l)));

  for (const [ctl, key] of [[sevSel, 'severity'], [layerSel, 'layer'], [durSel, 'duration']]) ctl.addEventListener('change', () => { f[key] = key === 'duration' ? Number(ctl.value) : ctl.value; paintPreview(); });
  for (const ctl of [title, message, pl, uk]) ctl.addEventListener('input', paintPreview);

  // Mini map with the circle
  const map = createMap(mapEl, { center: f.center, zoom: 14 });
  map.zoomControl?.remove?.();
  const circle = L.circle(f.center, { radius: f.radius, color: '#d03b3b', weight: 2, fillOpacity: 0.12 }).addTo(map);
  const marker = L.marker(f.center, { draggable: true }).addTo(map);
  const centreAddr = addressLine(f.center[0], f.center[1]);
  const where = searchBox({
    label: t('al.searchLabel'), placeholder: t('al.searchPlaceholder'), filters: false, near: () => f.center,
    onPick: (r) => { setCenter({ lat: r.lat, lng: r.lon }); map.setView(f.center, 15); }
  });
  const setCenter = (ll) => { f.center = [ll.lat ?? ll[0], ll.lng ?? ll[1]]; circle.setLatLng(f.center); marker.setLatLng(f.center); centreAddr.update(f.center[0], f.center[1]); updateReach(); };
  marker.on('dragend', () => setCenter(marker.getLatLng()));
  map.on('click', (e) => setCenter(e.latlng));
  setTimeout(() => { map.invalidateSize(); map.fitBounds(circle.getBounds().pad(0.3)); }, 50);
  cleanups.push(watchResize(map, mapEl), () => map.remove());

  radius.addEventListener('input', () => {
    f.radius = Number(radius.value);
    circle.setRadius(f.radius);
    radiusLabel.textContent = f.radius >= 1000 ? `${(f.radius / 1000).toFixed(1)} km` : `${f.radius} m`;
    map.fitBounds(circle.getBounds().pad(0.3), { animate: false });
    updateReach();
  });
  radiusLabel.textContent = `${f.radius} m`;

  const updateReach = debounce(async () => {
    if (!navigator.onLine) { reachEl.textContent = t('al.reachOffline'); return; }
    try {
      const r = await getReach(f.center[0], f.center[1], f.radius);
      reachEl.textContent = t('al.reach', { n: r.devicesInArea, total: r.devicesActive, min: r.windowMinutes });
    } catch { reachEl.textContent = ''; }
  }, 400);

  function paintPreview() {
    clear(preview);
    const sevClass = f.severity === 'Critical' ? 'critical' : f.severity === 'Info' ? 'info' : '';
    preview.append(h('p', { class: 'sect-title' }, t('al.preview')),
      h('div', { class: `banner ${sevClass}` }, icon(f.layer === 'Heat' ? 'thermo' : f.layer === 'Safety' ? 'moon' : 'bell'),
        h('div', { class: 'grow' }, h('b', null, title.value || t('al.titlePlaceholder')), h('span', { class: 'small' }, (getLang() === 'pl' && pl.value) || (getLang() === 'uk' && uk.value) || message.value || t('al.messagePlaceholder')))));
    armReset();
  }
  function armReset() { f.armed = false; sendBtn.lastChild.textContent = t('al.send'); sendBtn.classList.remove('danger'); }

  el.append(
    h('label', { class: 'field' }, t('al.template'), tplSel),
    h('div', { class: 'row wrap' },
      h('label', { class: 'field grow' }, t('al.severity'), sevSel),
      h('label', { class: 'field grow' }, t('al.layer'), layerSel),
      h('label', { class: 'field grow' }, t('al.duration'), durSel)),
    h('label', { class: 'field' }, t('al.title'), title),
    h('label', { class: 'field' }, t('al.message'), message),
    h('details', null, h('summary', { class: 'small', style: { cursor: 'pointer' } }, t('al.translations')),
      h('div', { class: 'stack', style: { marginTop: '.5rem' } }, h('label', { class: 'field' }, 'Polski', pl), h('label', { class: 'field' }, 'Українська', uk),
        h('p', { class: 'tiny muted' }, t('al.translationsHelp')))),
    h('div', { class: 'field' }, h('span', null, t('al.area')), h('span', { class: 'hint' }, t('al.areaHelp')),
      where, mapEl, centreAddr,
      h('div', { class: 'row small' }, icon('target', 'sm'), h('span', null, t('al.radius'), ': '), radiusLabel, h('span', { class: 'muted' }, ' · ', t('al.radiusHelp'))), radius, reachEl),
    preview, errorEl,
    h('div', { class: 'row wrap', style: { justifyContent: 'flex-end' } }, sendBtn));

  applyTemplate(f.template);
  if (f.template === 'custom') { sevSel.value = f.severity; paintPreview(); }
  updateReach();

  el.addEventListener('submit', async (e) => {
    e.preventDefault();
    errorEl.hidden = true;
    if (!requireOnline()) return;
    // Two-step send: the first press arms the button, the second sends. A mistaken tap should not alert a neighbourhood.
    if (!f.armed) {
      f.armed = true;
      sendBtn.lastChild.textContent = t('al.confirm');
      sendBtn.classList.add('danger');
      setTimeout(armReset, 5000);
      return;
    }
    sendBtn.disabled = true;
    try {
      const translations = {};
      if (pl.value.trim()) translations.pl = pl.value.trim();
      if (uk.value.trim()) translations.uk = uk.value.trim();
      const a = await createAlert({
        layer: f.layer || null, severity: f.severity, title: title.value, message: message.value, translations,
        latitude: f.center[0], longitude: f.center[1], radiusMeters: f.radius, durationMinutes: f.duration, cellId: cell?.cellId ?? null
      });
      toast(t('al.sent', { n: a.devicesInArea ?? 0 }));
      dataChanged();
      onSent?.(a);
    } catch (err) {
      errorEl.textContent = errorText(err, t);
      errorEl.hidden = false;
      armReset();
    } finally { sendBtn.disabled = false; }
  });

  el.destroy = () => cleanups.forEach((fn) => fn());
  return el;
}

export function mount(host) {
  const cleanups = [];
  let res = null;
  const page = h('div', { class: 'pl-page stack' });
  host.append(page);

  function render() {
    clear(page);
    page.append(h('div', { class: 'row between wrap' }, h('h1', null, t('al.title.page'))));
    page.append(h('p', { class: 'muted' }, t('al.pageHelp')));
    const formCard = h('section', { class: 'card' }, h('h2', null, t('al.new')));
    const form = alertForm({ onSent: () => {} });
    cleanups.push(() => form.destroy?.());
    formCard.append(form);

    const list = h('section', { class: 'card' }, h('h2', null, t('al.list')));
    if (!res) list.append(h('div', { class: 'skeleton', style: { height: '120px' } }));
    else {
      if (res.stale) list.append(staleBanner(res.savedAt));
      const active = res.data.filter((a) => a.status === 'Active' && new Date(a.expiresAt) > new Date());
      const past = res.data.filter((a) => !active.includes(a));
      list.append(h('p', { class: 'sect-title' }, t('al.active', { n: active.length })));
      list.append(active.length ? h('ul', { class: 'list' }, active.map((a) => alertRow(a, true))) : h('p', { class: 'small muted' }, t('al.noneActive')));
      if (past.length) list.append(h('details', { style: { marginTop: '.8rem' } }, h('summary', { class: 'small', style: { cursor: 'pointer' } }, t('al.past', { n: past.length })),
        h('ul', { class: 'list' }, past.slice(0, 20).map((a) => alertRow(a, false)))));
    }
    page.append(h('div', { class: 'grid-2 wide-left' }, formCard, list));
  }

  function alertRow(a, active) {
    return h('li', null,
      h('div', { class: 'row between wrap' },
        h('div', { class: 'grow' }, h('div', { class: 'row wrap' }, h('b', null, a.title), h('span', { class: `chip ${SEV[a.severity]}` }, t(`sev.${a.severity}`))),
          h('div', { class: 'small muted' }, `${a.radiusMeters >= 1000 ? (a.radiusMeters / 1000).toFixed(1) + ' km' : a.radiusMeters + ' m'} · ${a.status === 'Cancelled' ? t('al.cancelled') : active ? timeLeft(a.expiresAt, t) : t('time.expired')} · ${timeAgo(a.createdAt, t, getLang())}`),
          active ? h('div', { class: 'small' }, icon('users', 'sm'), ' ', t('al.phonesNow', { n: a.devicesInArea ?? 0 })) : null,
          h('p', { class: 'small', style: { marginTop: '.25rem' } }, a.message)),
        h('div', { class: 'row wrap' },
          h('button', { class: 'btn sm quiet', type: 'button', onclick: () => { P.focus = { lat: a.latitude, lon: a.longitude }; location.hash = '#/planner/map'; } }, icon('map', 'sm'), t('rep.showMap')),
          active ? h('button', { class: 'btn sm danger', type: 'button', onclick: () => cancel(a) }, t('al.cancel')) : null)));
  }

  async function cancel(a) {
    if (!requireOnline()) return;
    if (!confirm(t('al.cancelConfirm', { title: a.title }))) return;
    try { await cancelAlert(a.id); toast(t('al.cancelledToast')); dataChanged(); } catch (e) { toast(errorText(e, t), { error: true }); }
  }

  async function load() {
    try { res = await loadAlerts(); } catch (e) { res = { data: [], stale: false }; toast(errorText(e, t), { error: true }); }
    render();
  }
  cleanups.push(pOn('data', load));
  render();
  load();
  return () => cleanups.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });
}
