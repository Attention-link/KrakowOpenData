// "Report a concern" flow inside the resident panel: choose what you noticed, place the pin (drag it, tap the map or type an
// address), add a note, send. Only the report types of the chosen view are offered (Heat: heat reports, Night safety: night reports).
// Offline, the report is queued on the device and sent automatically when the connection is back.

import { h, icon, clear, toast, keepFocus, announce } from './util.js';
import { state, isOffline } from './state.js';
import { t } from './i18n.js';
import { postReport, errorText, NetworkError } from './api.js';
import { pinMarker } from './map.js';
import { outboxAdd } from './chrome.js';
import { searchBox } from './search.js';
import { addressLine } from './geo.js';
import { cellOf, cellBounds, LAYERS, layerOfApi } from './model.js';
import { voiceStep1, voiceStep2 } from './voice.js';

const FALLBACK_TYPES = [
  { type: 'LightOut', layer: 'Safety' }, { type: 'UnsafeAtNight', layer: 'Safety' }, { type: 'PathHazard', layer: 'Access' },
  { type: 'WaterNotWorking', layer: 'Heat' }, { type: 'NoShade', layer: 'Heat' }, { type: 'HeatSpot', layer: 'Heat' },
  { type: 'FloodedStreet', layer: 'Flood' }, { type: 'BlockedDrain', layer: 'Flood' }, { type: 'RisingWater', layer: 'Flood' },
  { type: 'SmokeOrBurning', layer: 'Air' }, { type: 'StrongFumes', layer: 'Air' }, { type: 'DustCloud', layer: 'Air' }
];

const TYPE_ICON = {
  LightOut: 'lamp', UnsafeAtNight: 'moon', PathHazard: 'access', WaterNotWorking: 'water', NoShade: 'sun', HeatSpot: 'thermo',
  FloodedStreet: 'wave', BlockedDrain: 'wave', RisingWater: 'wave', SmokeOrBurning: 'wind', StrongFumes: 'wind', DustCloud: 'wind'
};

export function renderReportView(ctx, body) {
  const rs = ctx.report ||= {
    step: 1, type: null, note: '',
    latlng: ctx.selected ? [ctx.selected.lat, ctx.selected.lon] : [ctx.map.getCenter().lat, ctx.map.getCenter().lng],
    sending: false, done: null, error: null
  };

  // The draggable pin on the map is the report location; the outlined square is what the report counts for.
  if (!ctx.reportPin && !rs.done) {
    ctx.reportPin = pinMarker(rs.latlng, { draggable: true, icon: 'flag', title: t('report.pin') }).addTo(ctx.map);
    ctx.reportPin.on('drag', () => { const p = ctx.reportPin.getLatLng(); drawSquare([p.lat, p.lng]); });
    ctx.reportPin.on('dragend', () => { const p = ctx.reportPin.getLatLng(); setLocation([p.lat, p.lng]); });
    ctx.map.on('click', onMapClick);
  }
  function onMapClick(e) {
    if (ctx.view !== 'report' || rs.done) return;
    setLocation([e.latlng.lat, e.latlng.lng], true);
  }
  function drawSquare(ll) {
    ctx.reportSquare?.remove();
    ctx.reportSquare = null;
    const meta = ctx.grid?.grid;
    if (!meta) return;
    const c = cellOf(meta, ll[0], ll[1]);
    ctx.reportSquare = L.rectangle(cellBounds(meta, c.row, c.col), { color: '#17171a', weight: 2, dashArray: '4 3', fill: false, interactive: false }).addTo(ctx.map);
  }
  function showInfo() {
    ctx.info.show({ lat: rs.latlng[0], lon: rs.latlng[1], radiusText: t('cov.report') });
  }
  function setLocation(ll, movePin = false) {
    rs.latlng = ll;
    if (movePin) ctx.reportPin?.setLatLng(ll);
    drawSquare(ll);
    showInfo();
    if (rs.step === 2 && !rs.done) paint();
  }
  ctx.reportCleanup = () => {
    ctx.reportPin?.remove();
    ctx.reportPin = null;
    ctx.reportSquare?.remove();
    ctx.reportSquare = null;
    ctx.map.off('click', onMapClick);
    ctx.report = null;
    ctx.info.hide();
  };

  // Only the types of the chosen view.
  const all = ctx.reportTypes?.length ? ctx.reportTypes : FALLBACK_TYPES;
  const layer = LAYERS[ctx.mode()].api;
  const types = all.filter((x) => x.layer === layer);
  if (rs.type && !types.some((x) => x.type === rs.type)) rs.type = null;

  const host = h('div', { class: 'stack' });
  body.append(host);
  ctx.setSheet('half');
  drawSquare(rs.latlng);
  showInfo();
  paint();

  // Every step rebuilds the form; keepFocus puts keyboard focus back on the same control (data-fk), else on the panel title.
  function paint() {
    keepFocus(host, paintNow, ctx.panelTitle);
  }

  function goStep(n) {
    rs.step = n;
    paint();
    announce(t('report.step', { n, total: 2 }));
  }

  function paintNow() {
    clear(host);
    if (rs.done) { host.append(donePane()); return; }

    host.append(h('div', { class: 'steps', role: 'img', 'aria-label': t('report.step', { n: rs.step, total: 2 }) },
      h('i', { class: 'on' }), h('i', { class: rs.step === 2 ? 'on' : '' })));

    if (rs.step === 1) {
      host.append(h('p', null, t(`report.intro.${ctx.mode()}`)),
        ...voiceStep1(rs, types, paint), // "Record by voice" (voice.js): fills the note, suggests the type
        h('div', { class: 'type-grid', role: 'group', 'aria-label': t('report.what') },
          types.map((ty) => h('button', { class: 'type-card', type: 'button', 'data-fk': `type-${ty.type}`, 'aria-pressed': String(rs.type === ty.type), onclick: () => { rs.type = ty.type; paint(); } },
            h('span', { class: `ico ${ty.layer.toLowerCase()}` }, icon(TYPE_ICON[ty.type] || 'flag')),
            h('b', null, t(`rtype.${ty.type}`)),
            h('span', { class: 'tiny muted' }, t(`mode.${layerOfApi(ty.layer) || 'safety'}`))))),
        h('div', { class: 'banner small danger' }, icon('alert', 'sm'), h('span', null, t('report.emergency'))),
        h('div', { class: 'row' }, h('div', { class: 'grow' }),
          h('button', { class: 'btn primary', type: 'button', 'data-fk': 'rep-next', disabled: rs.type ? null : true, onclick: () => goStep(2) }, t('common.next'), icon('right', 'sm'))));
      return;
    }

    const noteInput = h('textarea', { 'data-fk': 'rep-note', maxlength: '200', rows: '3', placeholder: t('report.notePlaceholder'), 'aria-describedby': 'note-count' });
    noteInput.value = rs.note;
    const count = h('span', { id: 'note-count', class: 'hint' }, `${rs.note.length}/200`);
    noteInput.addEventListener('input', () => { rs.note = noteInput.value; count.textContent = `${rs.note.length}/200`; });

    const sendBtn = h('button', { class: 'btn primary', type: 'button', 'data-fk': 'rep-send', disabled: rs.sending ? true : null, onclick: send },
      icon('send', 'sm'), rs.sending ? t('report.sending') : isOffline() ? t('report.saveOffline') : t('report.send'));

    const where = searchBox({
      label: t('report.searchLabel'), placeholder: t('report.searchPlaceholder'), near: () => rs.latlng, filters: false,
      onPick: (r) => { setLocation([r.lat, r.lon], true); ctx.map.panTo([r.lat, r.lon]); }
    });
    where.querySelector('input').dataset.fk = 'rep-q';

    host.append(
      h('div', { class: 'row' }, h('span', { class: 'chip' }, icon(TYPE_ICON[rs.type] || 'flag', 'sm'), t(`rtype.${rs.type}`)),
        h('button', { class: 'btn sm quiet', type: 'button', 'data-fk': 'rep-change', onclick: () => goStep(1) }, t('report.change'))),
      h('div', { class: 'card flat stack tight' }, h('b', null, t('report.where')),
        addressLine(rs.latlng[0], rs.latlng[1]),
        h('p', { class: 'small muted' }, icon('target', 'sm'), ' ', t('cov.report')),
        h('p', { class: 'small' }, t('report.drag')),
        where,
        h('div', { class: 'row wrap' },
          ctx.selected ? h('button', { class: 'btn sm', type: 'button', 'data-fk': 'rep-sel', onclick: () => moveTo([ctx.selected.lat, ctx.selected.lon]) }, t('report.useSelected')) : null,
          state.me ? h('button', { class: 'btn sm', type: 'button', 'data-fk': 'rep-mine', onclick: () => moveTo([state.me.lat, state.me.lon]) }, t('report.useMine')) : null)),
      h('label', { class: 'field' }, t('report.note'), noteInput, count),
      ...voiceStep2(rs, noteInput, count),
      h('p', { class: 'tiny muted' }, icon('shield', 'sm'), ' ', t('report.privacy')),
      rs.error ? h('p', { class: 'err', role: 'alert' }, rs.error) : '',
      isOffline() ? h('div', { class: 'banner small' }, icon('offline', 'sm'), h('span', null, t('report.offlineNote'))) : '',
      h('div', { class: 'row' }, h('button', { class: 'btn', type: 'button', 'data-fk': 'rep-back', onclick: () => goStep(1) }, icon('left', 'sm'), t('common.back')), h('div', { class: 'grow' }), sendBtn));
  }

  function moveTo(ll) {
    ctx.map.panTo(ll);
    setLocation(ll, true);
  }

  async function send() {
    rs.error = null;
    rs.sending = true;
    paint();
    const payload = { type: rs.type, latitude: rs.latlng[0], longitude: rs.latlng[1], note: rs.note.trim() || null, deviceId: state.deviceId };
    try {
      if (isOffline()) throw new NetworkError('offline');
      const created = await postReport(payload);
      rs.done = { queued: false, supporters: created.supporters };
    } catch (e) {
      if (e instanceof NetworkError) {
        await outboxAdd({ kind: 'report', body: payload });
        rs.done = { queued: true };
      } else {
        rs.error = errorText(e, t);
      }
    }
    rs.sending = false;
    ctx.reportPin?.remove();
    ctx.reportPin = null;
    if (rs.done && !rs.done.queued) ctx.refreshGrid();
    paint();
    // The result replaces the form: say it, and put focus on its heading.
    if (rs.done) {
      host.querySelector('h2')?.focus({ preventScroll: true });
      announce(rs.done.queued ? `${t('report.queued')}. ${t('report.queuedHelp')}` : t('report.thanks'));
    }
  }

  function donePane() {
    const d = rs.done;
    return h('div', { class: 'stack', style: { textAlign: 'center', alignItems: 'center', padding: '1rem 0' } },
      h('div', { class: 'ico', style: { width: '56px', height: '56px', borderRadius: '50%', background: d.queued ? 'var(--warn-soft)' : 'var(--ok-soft)', color: d.queued ? 'var(--warn-ink)' : 'var(--ok)', display: 'grid', placeItems: 'center' } },
        icon(d.queued ? 'clock' : 'check', 'lg')),
      h('h2', { tabindex: '-1' }, d.queued ? t('report.queued') : t('report.thanks')),
      h('p', { class: 'muted' }, d.queued ? t('report.queuedHelp') : d.supporters > 1 ? t('report.merged', { n: d.supporters }) : t('report.single')),
      h('div', { class: 'row wrap', style: { justifyContent: 'center' } },
        h('button', { class: 'btn primary', type: 'button', onclick: () => {
          const ll = rs.latlng;
          ctx.reportCleanup();
          ctx.view = 'place';
          ctx.selectPoint(ll[0], ll[1]);
        } }, d.queued ? t('common.done') : t('report.seeScore')),
        h('button', { class: 'btn', type: 'button', onclick: () => { const keep = rs.latlng; ctx.reportCleanup(); ctx.report = { step: 1, type: null, note: '', latlng: keep, sending: false, done: null, error: null }; ctx.render(); } }, t('report.another')),
        h('button', { class: 'btn quiet', type: 'button', onclick: () => ctx.showView('home') }, icon('left', 'sm'), t('nav.backMenu'))));
  }
}
