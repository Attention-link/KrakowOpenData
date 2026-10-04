// "Dostępność / Kraków bez barier": concrete barriers and amenities for wheelchair users, people with prams and people with
// limited mobility, on the map (distinct shapes AND symbols, grey for no data) and in an accessible list with the same items.
// Every item shows its facts, source, last OpenStreetMap edit and reliability, a link to correct it in OpenStreetMap and a
// "report a problem" action (sent as an unverified PathHazard report). The profile is a preference about barriers, never health
// data, and is kept on this device only. Also exports the barrier summary block used on walk routes (walk.js).

import { h, icon, clear, toast, openDialog, timeAgo, formatDistance, announce } from './util.js';
import { state, set, isOffline } from './state.js';
import { t, getLang, hasKey } from './i18n.js';
import { request, cachedGet, errorText, ApiError, NetworkError } from './api.js';
import { outboxAdd } from './chrome.js';
import { addressLine } from './geo.js';

// ── Profile: shared with the Accessibility tab (state.accessProfile, kept on this device; storage may be blocked, then it lasts for this visit) ──
export const PROFILES = ['wheelchair', 'pram', 'mobility'];

export const getProfile = () => (PROFILES.includes(state.accessProfile) ? state.accessProfile : 'wheelchair');

export function setProfile(p) {
  if (PROFILES.includes(p)) set({ accessProfile: p });
}

// ── API ──────────────────────────────────────────────────────────────────────
const qs = (o) => Object.entries(o).filter(([, v]) => v !== undefined && v !== null && v !== '')
  .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');

export const getAccessNearby = (lat, lon, radius, profile, kinds) =>
  request(`/api/safety/access?${qs({ lat: lat.toFixed(5), lon: lon.toFixed(5), radius, profile, kinds, limit: 300 })}`, { timeout: 30000 });

export const assessAccessPath = (path, profile) =>
  request('/api/safety/access/route', { method: 'POST', body: { path, profile }, timeout: 30000 });

// ── Symbols: a shape per status and a glyph per kind, so nothing depends on colour alone ──────────────
const MARK = { yes: '✓', limited: '!', no: '✕', unknown: '?' };
const GLYPH = {
  steps: 'M4 20h4v-4h4v-4h4V8h4',
  kerb: 'M3 18h18M3 14h7v-4h11',
  elevator: 'M6 3h12v18H6zM9.5 9.5L12 7l2.5 2.5M9.5 14.5L12 17l2.5-2.5',
  entrance: 'M6 21V4h10v17M3 21h18M13 12.5h.01',
  place: 'M12 4.5h.01M11 7.5v5.5h5l2 5M11 10.5h4M8.5 11.5a5 5 0 1 0 7.2 6.2',
  toilets: 'M12 3a2 2 0 1 0 0 .01M8 22v-7H6l2-6h8l2 6h-2v7',
  bench: 'M4 10h16M4 14h16M6 14v5M18 14v5M6 10V6',
  tactile: 'M7 7h.01M12 7h.01M17 7h.01M7 12h.01M12 12h.01M17 12h.01M7 17h.01M12 17h.01M17 17h.01',
  path: 'M4 19c3-6 5-7 8-7s5-1 8-7'
};

function glyphSvg(kind) {
  return `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="${GLYPH[kind] || GLYPH.place}"/></svg>`;
}

/** The marker as an HTML string for a Leaflet divIcon. Only known status and kind names reach the markup. */
function markerHtml(item) {
  const status = MARK[item.status] ? item.status : 'unknown';
  const kind = GLYPH[item.kind] ? item.kind : 'place';
  return `<div class="acc-mk ${status} k-${kind}" aria-hidden="true"><span class="acc-mk-shape"></span>${glyphSvg(kind)}<b class="acc-mk-mark">${MARK[status]}</b></div>`;
}

/** The same symbol for the list and the legend (an element, not a string). */
export function symbolEl(status, kind) {
  const el = h('span', { class: 'acc-sym', 'aria-hidden': 'true' });
  el.innerHTML = markerHtml({ status, kind });   // static, built from our own constants only
  return el;
}

// ── Wording ──────────────────────────────────────────────────────────────────
const YESNO_KEYS = new Set(['wheelchair', 'handrail', 'tactile', 'changing_table', 'backrest', 'toilets_wheelchair']);

export function factLabel(f) {
  const k = `acc.fact.${f.key}`;
  return hasKey(k) ? t(k) : f.label;
}

export function factText(f) {
  if (f.value === 'unknown') return t('acc.status.unknown');
  const tr = (key) => (hasKey(key) ? t(key) : null);
  if (f.key === 'category') return tr(`acc.cat.${f.value}`) || String(f.value).replace(/_/g, ' ');
  if (getLang() === 'pl') return f.text;
  if (YESNO_KEYS.has(f.key)) return tr(`acc.yn.${f.value}`) || f.value;
  if (f.key === 'kerb') return tr(`acc.kerb.${f.value}`) || f.value;
  if (f.key === 'ramp') return tr(`acc.ramp.${f.value}`) || f.value;
  if (f.key === 'surface') return tr(`acc.surf.${f.value}`) || f.value;
  if (f.key === 'width') return `${f.value} m`;
  if (f.key === 'incline') return `${f.value}%`;
  return f.value;
}

const kindName = (kind) => t(`acc.kind.${kind}`);
const statusWord = (s) => t(`acc.status.${s}`);
const dateText = (iso) => (iso ? new Date(iso).toLocaleDateString(getLang() === 'uk' ? 'uk-UA' : getLang() === 'pl' ? 'pl-PL' : 'en-GB') : t('acc.status.unknown'));

function statusEl(item) {
  return h('span', { class: 'acc-status-wrap' },
    h('span', { class: `acc-status ${item.status}` }, h('span', { 'aria-hidden': 'true' }, `♿ ${MARK[item.status] || '?'}`), ' ', statusWord(item.status)),
    item.barrier ? h('span', { class: 'tiny acc-barrier' }, t('acc.barrier')) : null);
}

function reliabilityEl(item) {
  return h('div', { class: 'acc-source' },
    h('div', null, item.source),
    h('div', { class: 'tiny muted' }, t('acc.lastEdit', { date: dateText(item.lastEdited) })),
    h('span', { class: `acc-rel ${item.reliability}` }, t(`acc.rel.${item.reliability}`)));
}

function factsEl(item) {
  return h('ul', { class: 'acc-facts' }, item.facts.map((f) =>
    h('li', { class: f.value === 'unknown' ? 'unknown' : null }, h('span', { class: 'muted' }, `${factLabel(f)}: `), factText(f))));
}

function itemTitle(item) {
  return item.name ? `${item.name} (${kindName(item.kind)})` : kindName(item.kind);
}

/** Popup / detail content shared by the map and the route list. */
function detailEl(item, onReport) {
  return h('div', { class: 'acc-detail' },
    h('div', { class: 'poi-title' }, itemTitle(item)),
    statusEl(item),
    factsEl(item),
    reliabilityEl(item),
    h('div', { class: 'row wrap', style: { marginTop: '.4rem' } },
      fixLink(item),
      onReport ? h('button', { class: 'btn sm', type: 'button', onclick: () => onReport(item) }, icon('flag', 'sm'), t('acc.report')) : null));
}

function fixLink(item) {
  return h('a', { class: 'btn sm', href: item.editUrl, target: '_blank', rel: 'noopener' }, icon('external', 'sm'), t('acc.fix'),
    h('span', { class: 'sr-only' }, ` ${t('acc.newTab')}`));
}

// ── Report a problem (closed vocabulary, no personal data; sent as an unverified PathHazard report) ──
export function openAccessReport(item) {
  const options = ['wrong', 'outdated', 'temporary', 'missing'];
  let choice = null;
  openDialog((close) => {
    const err = h('p', { class: 'small', role: 'alert', style: { color: 'var(--danger)' } });
    const note = h('textarea', { id: 'acc-rep-note', rows: '2', maxlength: '120' });
    const radios = h('fieldset', { class: 'acc-fieldset' }, h('legend', null, t('acc.rep.what')),
      options.map((o) => h('label', { class: 'acc-radio' },
        h('input', { type: 'radio', name: 'acc-rep', value: o, onchange: () => { choice = o; err.textContent = ''; } }), ' ', t(`acc.rep.${o}`))));
    async function send(btn) {
      if (!choice) { err.textContent = t('acc.rep.pick'); return; }
      btn.disabled = true;
      const extra = note.value.trim();
      const text = `[Dostępność] ${item.kind} ${item.id}: ${choice}${extra ? ` - ${extra}` : ''}`.slice(0, 200);
      const payload = { type: 'PathHazard', latitude: item.latitude, longitude: item.longitude, note: text, deviceId: state.deviceId };
      try {
        if (isOffline()) throw new NetworkError('offline');
        await request('/api/safety/reports', { method: 'POST', body: payload });
        toast(t('acc.rep.sent'));
        close('sent');
      } catch (e) {
        if (e instanceof NetworkError) {
          await outboxAdd({ kind: 'report', body: payload });
          toast(t('acc.rep.queued'));
          close('queued');
        } else {
          err.textContent = errorText(e, t);
          btn.disabled = false;
        }
      }
    }
    const sendBtn = h('button', { class: 'btn primary', type: 'button', onclick: (e) => send(e.currentTarget) }, icon('send', 'sm'), t('acc.rep.send'));
    return {
      title: t('acc.rep.title'),
      body: h('div', { class: 'stack' },
        h('p', null, h('b', null, itemTitle(item)), ' · ', statusWord(item.status)),
        radios,
        h('div', { class: 'field' }, h('label', { for: 'acc-rep-note' }, t('acc.rep.note')), note),
        h('p', { class: 'small muted' }, t('acc.rep.info')),
        err),
      footer: [h('button', { class: 'btn', type: 'button', onclick: () => close('cancel') }, t('common.cancel')), sendBtn]
    };
  });
}

// ── The view ─────────────────────────────────────────────────────────────────
const FILTERS = {
  default: 'steps,kerb,elevator,entrance,place,toilets,bench',
  barriers: 'steps,kerb,elevator,entrance,place,toilets,path',
  path: 'path',
  all: ''
};
const RADII = [100, 300, 500, 1000];
const PAGE = 40;

export function renderAccessView(ctx, body) {
  const as = ctx.accessState ||= {
    center: ctx.selected ? [ctx.selected.lat, ctx.selected.lon] : state.me ? [state.me.lat, state.me.lon] : [ctx.map.getCenter().lat, ctx.map.getCenter().lng],
    radius: 300, filter: 'default', data: null, savedAt: null, stale: false, loading: false, error: null, picking: false, shown: PAGE, token: 0, retry: null
  };
  const layer = ctx.accessLayer ||= L.layerGroup().addTo(ctx.map);
  const markers = new Map();

  // The coloured score grid would compete with the symbols: hide it while this view is open.
  ctx.gridLayer?.group?.remove();
  ctx.accessCleanup = () => {
    clearTimeout(as.retry);
    layer.clearLayers();
    ctx.map.removeLayer(layer);
    ctx.accessLayer = null;
    ctx.accessCircle?.remove();
    ctx.accessCircle = null;
    ctx.gridLayer?.group?.addTo(ctx.map);
    ctx.pick = null;
    ctx.accessState = null;
    ctx.accessCleanup = null;
    ctx.info.hide();
  };
  ctx.pick = (latlng) => {
    if (ctx.view !== 'access') return false;
    as.center = [latlng.lat, latlng.lng];
    as.picking = false;
    load();
    return true;
  };

  const host = h('div', { class: 'stack acc-view' });
  body.append(host);
  // A request started by an earlier render must paint into this one (the panel is rebuilt on language or connection changes).
  as.ui = { paint, drawMap, drawCenter };
  ctx.setSheet('half');
  if (!as.data && !as.loading) load(); else { drawMap(); paint(); }

  async function load() {
    const token = ++as.token;
    clearTimeout(as.retry);
    as.loading = true; as.error = null; as.shown = PAGE;
    as.ui.paint();
    as.ui.drawCenter();
    const [lat, lon] = as.center;
    const profile = getProfile();
    const kinds = FILTERS[as.filter];
    try {
      const key = `access:${profile}:${lat.toFixed(4)},${lon.toFixed(4)}:${as.radius}:${as.filter}`;
      const { data, savedAt, stale } = await cachedGet(key, () => getAccessNearby(lat, lon, as.radius, profile, kinds));
      if (token !== as.token) return;
      Object.assign(as, { data, savedAt, stale, loading: false });
    } catch (e) {
      if (token !== as.token) return;
      as.loading = false;
      if (e instanceof ApiError && e.status === 503) {
        as.error = t('acc.preparing');
        as.retry = setTimeout(load, (e.retryAfter || 20) * 1000);
      } else as.error = errorText(e, t);
    }
    as.ui.drawMap();
    as.ui.paint();
  }

  // ── Map ──
  function drawCenter() {
    ctx.accessCircle?.remove();
    ctx.accessCircle = L.circle(as.center, { radius: as.radius, color: '#17171a', weight: 1.5, opacity: 0.7, dashArray: '6 6', fill: false, interactive: false }).addTo(ctx.map);
    ctx.info.show({ lat: as.center[0], lon: as.center[1], radiusMeters: as.radius });
    // Bring the searched circle into view (the city overview piles every symbol on one spot).
    const bounds = ctx.accessCircle.getBounds();
    if (!ctx.map.getBounds().contains(bounds) || ctx.map.getZoom() < 15) ctx.map.fitBounds(bounds, { padding: [24, 24], maxZoom: 18 });
  }

  function drawMap() {
    layer.clearLayers();
    markers.clear();
    drawCenter();
    const items = visibleItems();
    for (const item of items) {
      const m = L.marker([item.latitude, item.longitude], {
        icon: L.divIcon({ html: markerHtml(item), className: '', iconSize: [28, 28], iconAnchor: [14, 14] }),
        title: `${itemTitle(item)}: ${statusWord(item.status)}`, keyboard: false, zIndexOffset: item.barrier ? 300 : item.status === 'unknown' ? 0 : 100
      });
      m.bindPopup(() => detailEl(item, openAccessReport), { minWidth: 220, maxWidth: 300 });
      m.addTo(layer);
      markers.set(item.id, m);
    }
  }

  function visibleItems() {
    const items = as.data?.items || [];
    return as.filter === 'barriers' ? items.filter((i) => i.barrier) : items;
  }

  function showOnMap(item) {
    ctx.map.setView([item.latitude, item.longitude], Math.max(ctx.map.getZoom(), 18));
    markers.get(item.id)?.openPopup();
    if (matchMedia('(max-width: 959.98px)').matches) ctx.setSheet('peek');
  }

  // ── Panel ──
  function paint() {
    clear(host);
    host.append(h('p', { class: 'small' }, t('acc.intro')), profilePicker(), whereCard());
    if (as.loading) host.append(h('p', { class: 'small muted', role: 'status' }, t('acc.loading')), h('div', { class: 'skeleton', style: { height: '120px' } }));
    if (as.error) host.append(h('div', { class: 'banner danger', role: 'alert' }, icon('alert'), h('span', null, as.error)));
    const d = as.data;
    if (d && !as.loading) {
      if (d.sampleData) host.append(h('div', { class: 'banner warn' }, icon('alert'), h('span', null, t('acc.sample'))));
      if (as.stale) host.append(h('div', { class: 'banner small' }, icon('offline', 'sm'), h('span', null, t('acc.saved', { when: timeAgo(as.savedAt, t, getLang()) }))));
      if (d.dataNoteCode) host.append(h('div', { class: 'banner warn', role: 'note' }, icon('info'), h('span', null, getLang() === 'pl' && d.dataNote ? d.dataNote : t(`acc.note.${d.dataNoteCode}`))));
      host.append(summaryCard(d), legendCard(), listSection(d), reportsSection(d));
    }
    host.append(h('p', { class: 'tiny muted' }, t('acc.noDataNote')), h('p', { class: 'tiny muted' }, t('acc.licence')));
  }

  function profilePicker() {
    const current = getProfile();
    return h('fieldset', { class: 'acc-fieldset card flat' },
      h('legend', null, t('acc.profile')),
      PROFILES.map((p) => h('label', { class: 'acc-radio' },
        h('input', { type: 'radio', name: 'acc-profile', value: p, checked: p === current ? true : null,
          onchange: () => { setProfile(p); load(); } }),
        h('span', null, h('b', null, t(`acc.profile.${p}`)), h('span', { class: 'tiny muted', style: { display: 'block' } }, t(`acc.profile.${p}.hint`))))),
      h('p', { class: 'tiny muted' }, icon('lock', 'sm'), ' ', t('acc.privacy')));
  }

  function whereCard() {
    const radius = h('select', { id: 'acc-radius', onchange: (e) => { as.radius = Number(e.target.value); load(); } },
      RADII.map((r) => h('option', { value: String(r), selected: r === as.radius ? true : null }, formatDistance(r))));
    const filter = h('select', { id: 'acc-filter', onchange: (e) => { as.filter = e.target.value; load(); } },
      Object.keys(FILTERS).map((f) => h('option', { value: f, selected: f === as.filter ? true : null }, t(`acc.filter.${f}`))));
    return h('div', { class: 'card flat stack tight' },
      h('div', { class: 'small muted' }, t('acc.where')),
      addressLine(as.center[0], as.center[1]),
      h('div', { class: 'row wrap' },
        h('button', { class: `btn sm ${as.picking ? 'primary' : ''}`, type: 'button', 'aria-pressed': String(as.picking), onclick: () => { as.picking = !as.picking; paint(); } },
          icon('pin', 'sm'), as.picking ? t('acc.picking') : t('acc.pick')),
        h('button', { class: 'btn sm', type: 'button', onclick: () => { const c = ctx.map.getCenter(); as.center = [c.lat, c.lng]; load(); } }, icon('target', 'sm'), t('acc.useMap')),
        state.me ? h('button', { class: 'btn sm', type: 'button', onclick: () => { as.center = [state.me.lat, state.me.lon]; load(); } }, icon('locate', 'sm'), t('acc.useMine')) : null),
      h('div', { class: 'row wrap' },
        h('div', { class: 'field filter' }, h('label', { for: 'acc-radius' }, t('acc.radius')), radius),
        h('div', { class: 'field filter' }, h('label', { for: 'acc-filter' }, t('acc.show')), filter)));
  }

  function summaryCard(d) {
    const covLines = d.coverage.filter((c) => c.total > 0).map((c) => {
      const what = t(`acc.cov.${c.key}`);
      const thin = c.total >= 5 && c.known / c.total < 0.3;
      return h('li', { class: thin ? 'thin' : null }, t('acc.coverage.line', { k: c.known, n: c.total, what: what === `acc.cov.${c.key}` ? c.label : what }));
    });
    return h('div', { class: 'card flat stack tight' },
      h('h3', null, t('acc.summary')),
      d.counts.length
        ? h('ul', { class: 'acc-counts' }, d.counts.map((c) => h('li', null,
          symbolEl(c.no ? 'no' : c.limited ? 'limited' : c.yes ? 'yes' : 'unknown', c.kind),
          h('span', null, h('b', null, `${kindName(c.kind)}: ${c.total}`),
            h('span', { class: 'tiny muted' }, ` (${[['yes', c.yes], ['limited', c.limited], ['no', c.no], ['unknown', c.unknown]].filter(([, n]) => n).map(([s, n]) => `${statusWord(s)} ${n}`).join(', ')})`)))))
        : h('p', { class: 'small muted' }, t('acc.empty')),
      covLines.length ? h('div', null, h('b', { class: 'small' }, t('acc.coverage')), h('ul', { class: 'acc-cov small' }, covLines)) : null);
  }

  function legendCard() {
    return h('details', { class: 'card flat' },
      h('summary', null, t('acc.legend')),
      h('ul', { class: 'acc-legend small' },
        ['yes', 'limited', 'no', 'unknown'].map((s) => h('li', null, symbolEl(s, 'place'), t(`acc.legend.${s}`))),
        Object.keys(GLYPH).map((k) => h('li', null, symbolEl('yes', k), kindName(k)))));
  }

  function listSection(d) {
    const items = visibleItems();
    const shown = items.slice(0, as.shown);
    const section = h('section', { class: 'stack tight', 'aria-labelledby': 'acc-list-title' },
      h('h3', { id: 'acc-list-title' }, t('acc.list')));
    if (!items.length) { section.append(h('p', { class: 'small muted' }, t('acc.empty'))); return section; }
    const table = h('table', { class: 't acc-table' },
      h('caption', { class: 'small muted' }, t('acc.listCaption', { n: d.totalMatched, shown: shown.length })),
      h('thead', null, h('tr', null,
        ['item', 'status', 'facts', 'distance', 'source', 'actions'].map((c) => h('th', { scope: 'col' }, t(`acc.col.${c}`))))),
      h('tbody', null, shown.map((item) => h('tr', { class: `acc-row ${item.status}` },
        h('th', { scope: 'row' }, h('span', { class: 'row', style: { gap: '.4rem', flexWrap: 'nowrap' } }, symbolEl(item.status, item.kind), h('span', null, itemTitle(item)))),
        h('td', null, statusEl(item)),
        h('td', null, factsEl(item)),
        h('td', { class: 'num' }, formatDistance(item.distanceMeters)),
        h('td', null, reliabilityEl(item)),
        h('td', null, h('div', { class: 'acc-actions' },
          h('button', { class: 'btn sm quiet', type: 'button', onclick: () => showOnMap(item), 'aria-label': `${t('acc.showOnMap')}: ${itemTitle(item)}` }, icon('map', 'sm'), t('acc.showOnMap')),
          fixLink(item),
          h('button', { class: 'btn sm quiet', type: 'button', onclick: () => openAccessReport(item), 'aria-label': `${t('acc.report')}: ${itemTitle(item)}` }, icon('flag', 'sm'), t('acc.report'))))))));
    section.append(h('div', { class: 'acc-table-wrap' }, table));
    if (items.length > shown.length) {
      section.append(h('button', { class: 'btn sm', type: 'button', onclick: () => { as.shown += PAGE; paint(); } }, t('acc.more')));
    }
    section.append(h('p', { class: 'tiny muted' }, t('acc.lastEditNote')));
    return section;
  }

  function reportsSection(d) {
    if (!d.userReports?.length) return null;
    return h('section', { class: 'card acc-user-reports stack tight', 'aria-labelledby': 'acc-rep-title' },
      h('h3', { id: 'acc-rep-title' }, t('acc.reports')),
      h('p', { class: 'small muted' }, t('acc.reportsNote')),
      h('ul', { class: 'list small' }, d.userReports.map((r) => h('li', null,
        h('span', { class: 'acc-rel user_unverified' }, t('acc.rel.user_unverified')), ' ',
        t('acc.reportLine', { type: t(`rtype.${r.type}`), d: formatDistance(r.distanceMeters), when: timeAgo(r.createdAt, t, getLang()), n: r.supporters })))));
  }
}

// ── Barrier summary for a walking route (used by walk.js) ────────────────────
const routeCache = new Map();

const routeKey = (route, profile) => `${profile}:${route.lengthMeters}:${route.path[0]}:${route.path[route.path.length - 1]}`;

function summaryText(line) {
  if (getLang() === 'pl' && line.text) return line.text;
  const n = line.count, m = formatDistance(line.meters), p = Math.round(line.share * 100);
  return t(`acc.sum.${line.code}`, { n, m, p });
}

/**
 * A card with the barrier summary of one route for the chosen profile, e.g. "2 schody bez rampy, 1 wysoki krawężnik,
 * 120 m kostki brukowej, 30% bez danych", with a clear "no data ≠ accessible" note. Fetched once per route and profile.
 * When a Leaflet layer is given, the barriers are drawn on it too.
 */
export function accessRouteBlock(route, { layer, onChangeProfile } = {}) {
  const profile = getProfile();
  const key = routeKey(route, profile);
  // Not a live region: the card holds the whole barrier list and links. A short summary is announced once instead.
  const card = h('section', { class: 'card flat acc-route stack tight', 'aria-labelledby': 'acc-route-title' });
  const fill = (state_) => {
    clear(card);
    card.append(h('div', { class: 'row between wrap' },
      h('h3', { id: 'acc-route-title' }, h('span', { 'aria-hidden': 'true' }, '♿ '), t('acc.route.title')),
      h('span', { class: 'small muted' }, t('acc.route.for', { p: t(`acc.profile.${profile}`) }))));
    if (state_.loading) { card.append(h('p', { class: 'small muted' }, t('acc.route.loading'))); return; }
    if (state_.error) { card.append(h('p', { class: 'small' }, t('acc.route.failed'))); return; }
    const r = state_.data;
    if (r.sampleData) card.append(h('div', { class: 'banner warn small' }, t('acc.sample')));
    if (!r.inDataArea) card.append(h('div', { class: 'banner warn small' }, icon('info', 'sm'), h('span', null, t('acc.route.outside'))));
    const barrierLines = r.summary.filter((l) => l.severity !== 'info' && l.code !== 'unknown_share');
    const otherLines = r.summary.filter((l) => !barrierLines.includes(l));
    if (!barrierLines.length && r.inDataArea) card.append(h('p', { class: 'small' }, icon('check', 'sm'), ' ', t('acc.route.none')));
    card.append(h('ul', { class: 'acc-sum' },
      [...barrierLines, ...otherLines].map((l) => h('li', { class: `sev-${l.severity}` },
        h('span', { class: `acc-sev ${l.severity}` }, t(`acc.sev.${l.severity}`)), ' ', summaryText(l)))));
    card.append(h('p', { class: 'banner small' }, icon('info', 'sm'), h('span', null, t('acc.noDataNote'))));
    if (r.barriers.length) {
      card.append(h('details', null, h('summary', { class: 'small' }, `${t('acc.route.barriers')} (${r.barriers.length})`),
        h('ol', { class: 'acc-route-list small' }, r.barriers.map((b) => h('li', null,
          symbolEl(b.status, b.kind), ' ', h('b', null, itemTitle(b)), ` · ${statusWord(b.status)} · ${t('acc.route.at', { m: formatDistance(b.alongRouteMeters) })} · `,
          b.facts.filter((f) => f.value !== 'unknown').slice(0, 2).map((f) => `${factLabel(f)}: ${factText(f)}`).join(', '), ' ',
          h('a', { href: b.editUrl, target: '_blank', rel: 'noopener' }, t('acc.fix'), h('span', { class: 'sr-only' }, ` ${t('acc.newTab')}`)))))));
    }
    card.append(h('p', { class: 'tiny muted' }, t('acc.route.method')));
    if (onChangeProfile) card.append(h('button', { class: 'btn sm quiet', type: 'button', onclick: onChangeProfile }, t('acc.route.change')));
    if (layer) drawRouteBarriers(layer, r);
  };

  const cached = routeCache.get(key);
  if (cached) fill({ data: cached });
  else {
    fill({ loading: true });
    assessAccessPath(route.path, profile)
      .then((data) => {
        routeCache.set(key, data); if (routeCache.size > 20) routeCache.delete(routeCache.keys().next().value); fill({ data });
        const lines = data.summary.filter((l) => l.severity !== 'info' && l.code !== 'unknown_share').map(summaryText);
        announce(`${t('acc.route.title')}: ${lines.length ? lines.join(', ') : t('acc.route.none')}`);
      })
      .catch(() => fill({ error: true }));
  }
  return card;
}

let routeGroup = null;

/** Barrier symbols of the shown route, in their own group so a repaint replaces them instead of adding duplicates. */
function drawRouteBarriers(layer, r) {
  routeGroup ||= L.layerGroup();
  routeGroup.clearLayers();
  if (!layer.hasLayer(routeGroup)) routeGroup.addTo(layer);
  for (const b of r.barriers) {
    L.marker([b.latitude, b.longitude], {
      icon: L.divIcon({ html: markerHtml(b), className: '', iconSize: [28, 28], iconAnchor: [14, 14] }),
      title: `${itemTitle(b)}: ${statusWord(b.status)}`, keyboard: false, zIndexOffset: 700
    }).bindPopup(() => detailEl(b, openAccessReport), { minWidth: 220, maxWidth: 300 }).addTo(routeGroup);
  }
}
