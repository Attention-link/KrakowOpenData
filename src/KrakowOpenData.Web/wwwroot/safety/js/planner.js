// Planner dashboard shell: sign-in, navigation (side rail on wide screens, bottom bar on phones),
// the event selector (heat / night / both) and the live conditions strip. Pages are loaded on demand.

import { h, icon, clear, toast, timeAgo } from './util.js';
import { state, set, on, isOffline } from './state.js';
import { t, getLang } from './i18n.js';
import { plannerPing, errorText, ApiError } from './api.js';
import { topbar, statusPill } from './chrome.js';
import { P, pOn, pEmit, loadSummary, closeDrawer } from './planner-common.js';
import { explainable, openKpiExplainer } from './explain.js';

const PAGES = [
  ['overview', 'dash', 'pl.nav.overview'],
  ['map', 'map', 'pl.nav.map'],
  ['reports', 'flag', 'pl.nav.reports'],
  ['alerts', 'bell', 'pl.nav.alerts'],
  ['weights', 'gear', 'pl.nav.weights'],
  ['contacts', 'phone', 'pl.nav.contacts']
];

const EVENTS = [['heat', 'sun'], ['night', 'moon'], ['flood', 'wave'], ['air', 'wind']];
let autoKeyRejected = false;   // the configured demo key was refused by the API: show the sign-in form instead of looping

export function mountPlanner(root, page) {
  // Demo access: when the host configures a demo key, the dashboard signs in with it, so the menu link opens it directly.
  const autoKey = (window.KRK_CONFIG || {}).plannerAutoKey || '';
  if (!state.plannerKey && autoKey && !autoKeyRejected) set({ plannerKey: autoKey });
  if (!state.plannerKey) return mountLogin(root);
  const demoAccess = !!autoKey && state.plannerKey === autoKey;
  const cleanups = [];
  const pageName = PAGES.some((p) => p[0] === page) ? page : 'overview';

  const pill = statusPill();
  cleanups.push(() => pill.destroy());
  const signOut = demoAccess ? null : h('button', { class: 'btn sm quiet', type: 'button', onclick: () => { set({ plannerKey: null }); location.hash = '#/planner'; window.dispatchEvent(new Event('hashchange')); } },
    icon('logout', 'sm'), h('span', { class: 'hide-sm' }, t('pl.signOut')));
  const toResident = h('a', { class: 'btn sm quiet', href: '#/' }, icon('users', 'sm'), h('span', { class: 'hide-sm' }, t('pl.residentView')));
  // The dashboard is for city staff, not for everyone: the bar is marked so nobody mistakes it for the public app.
  const staffChip = h('span', { class: 'chip staff', title: t('pl.staffHelp') }, icon('lock', 'sm'), h('span', null, t('pl.staffOnly')));
  const bar = topbar({ subtitle: t('pl.subtitle'), right: [staffChip, toResident, signOut], pill });
  bar.classList.add('staff');

  // Navigation
  const badges = {};
  const nav = h('nav', { class: 'pl-nav', 'aria-label': t('pl.nav.label') },
    PAGES.map(([id, ic, key]) => {
      const badge = h('span', { class: 'badge-n', hidden: true });
      badges[id] = badge;
      return h('a', { href: `#/planner/${id}`, 'aria-current': id === pageName ? 'page' : null }, icon(ic), h('span', null, t(key)), badge);
    }));

  // Tools: event selector, conditions, freshness
  const eventSeg = h('div', { class: 'seg', role: 'group', 'aria-label': t('pl.event') });
  const paintEvents = () => {
    clear(eventSeg);
    for (const [ev, ic] of EVENTS) {
      eventSeg.append(h('button', { type: 'button', 'data-mode': ev === 'night' ? 'safety' : ev, 'aria-pressed': String(P.event === ev), onclick: () => changeEvent(ev) }, icon(ic, 'sm'), t(`event.${ev}`)));
    }
  };
  const conds = h('div', { class: 'row wrap grow' });
  const updated = h('span', { class: 'tiny muted' });
  const refreshBtn = h('button', { class: 'btn sm quiet', type: 'button', 'aria-label': t('pl.refresh'), onclick: () => refresh(true) }, icon('refresh', 'sm'));
  // Every page except the dashboard gets a clear way back.
  const backBtn = pageName === 'overview' ? null : h('a', { class: 'btn sm', href: '#/planner/overview' }, icon('left', 'sm'), t('nav.backDashboard'));
  const demoChip = demoAccess ? h('span', { class: 'chip', title: t('pl.demoAccess') }, icon('info', 'sm'), t('pl.demoChip')) : null;
  const tools = h('div', { class: 'pl-tools' }, backBtn, h('span', { class: 'small muted hide-sm' }, t('pl.planningFor')), eventSeg, conds, demoChip, updated, refreshBtn);

  const body = h('main', { class: 'pl-body', id: 'main', tabindex: '-1' }, h('h1', { class: 'sr-only' }, `${t('app.title')} · ${t('a11y.planner')}`));
  const col = h('div', { class: 'pl-col' }, tools, body);
  P.root = col;
  P.event = state.event;
  const shell = h('div', { class: 'pl' }, nav, col);
  root.append(bar, shell);
  paintEvents();

  // Data -----------------------------------------------------------------------
  function paintConditions() {
    clear(conds);
    const s = P.summary?.data;
    if (!s) return;
    const c = s.conditions;
    const hot = c.heat.level >= 1;
    // Only what matters for the planning event: Heat shows the heat situation, Night safety daylight, Flood the rivers, Air the air quality.
    // Each chip opens an explanation of where the figure comes from.
    if (P.event === 'heat') {
      const chip = h('span', { class: `chip ${hot ? 'warn' : 'heat'}` }, icon('thermo', 'sm'), `${c.heat.temperatureC !== null ? Math.round(c.heat.temperatureC) + '°C · ' : ''}${t(`heat.${c.heat.pressure}`)}`);
      conds.append(explainable(chip, () => openKpiExplainer('temperature', {
        value: c.heat.temperatureC !== null ? `${c.heat.temperatureC.toFixed(1)} °C` : '–',
        extra: [[t('cond.heat'), t(`heat.${c.heat.pressure}`)], c.heat.warningTitle ? [t('cond.warningsTitle'), c.heat.warningTitle] : null, [t('cond.airTitle'), c.air.pm25 !== null ? `${t(`air.${c.air.band}`)} · PM2.5 ${c.air.pm25} µg/m³ (${c.air.station || ''})` : t('air.Unknown')]].filter(Boolean)
      }), t('cond.temp.title')));
    }
    if (P.event === 'night') {
      const chip = h('span', { class: `chip ${c.isDark ? 'safety' : ''}` }, icon(c.isDark ? 'moon' : 'sun', 'sm'), c.isDark ? t('cond.dark', { time: c.sunriseLocal || '' }) : t('cond.light', { time: c.sunsetLocal || '' }));
      conds.append(explainable(chip, () => openKpiExplainer('daylight', { value: `${c.sunriseLocal ?? '–'} → ${c.sunsetLocal ?? '–'}`, extra: [[t('cond.nowDark'), c.isDark ? '✓' : '–']] }), t('cond.daylight.title')));
    }
    if (P.event === 'flood') {
      const high = c.hydro.elevatedGauges > 0;
      const chip = h('span', { class: `chip ${high ? 'danger' : 'flood'}` }, icon('wave', 'sm'), high ? t('cond.rivers', { n: c.hydro.elevatedGauges }) : t('cond.riversOk'));
      conds.append(explainable(chip, () => openKpiExplainer('riverLevel', { value: `${c.hydro.elevatedGauges}`, extra: [[t('cond.riversTitle'), high ? t('cond.rivers', { n: c.hydro.elevatedGauges }) : t('cond.riversOk')], [t('cond.worstState'), t(`hydro.${c.hydro.worstState}`)]] }), t('cond.rivers.title')));
    }
    if (P.event === 'air') {
      const bad = c.air.band === 'Poor' || c.air.band === 'VeryPoor';
      const chip = h('span', { class: `chip ${bad ? 'danger' : 'air'}` }, icon('wind', 'sm'), c.air.pm25 !== null && c.air.pm25 !== undefined ? `${t(`air.${c.air.band}`)} · PM2.5 ${Math.round(c.air.pm25)}` : t('air.Unknown'));
      conds.append(explainable(chip, () => openKpiExplainer('airLevel', { value: c.air.pm25Average != null ? `${c.air.pm25Average} µg/m³` : '–', extra: [[t('cond.airTitle'), c.air.pm25 !== null && c.air.pm25 !== undefined ? `${t(`air.${c.air.band}`)} · PM2.5 ${c.air.pm25} µg/m³` : t('air.Unknown')], c.air.station ? [t('cond.station'), c.air.station] : null].filter(Boolean) }), t('cond.air.title')));
    }
    if (c.dataGaps?.length) conds.append(h('span', { class: 'chip warn', title: c.dataGaps.join(', ') }, icon('alert', 'sm'), t('pl.incomplete')));
    updated.textContent = P.summary.stale ? t('pl.savedAt', { when: timeAgo(P.summary.savedAt, t, getLang()) }) : t('pl.updated', { when: timeAgo(P.summary.savedAt, t, getLang()) });
    if (badges.alerts) { badges.alerts.hidden = !s.activeAlerts; badges.alerts.textContent = s.activeAlerts; }
    const openReports = s.kpis.find((k) => k.key === 'openReports')?.value || 0;
    if (badges.reports) { badges.reports.hidden = !openReports; badges.reports.textContent = openReports; }
  }

  let loading = false;
  async function refresh(announceDone = false) {
    if (loading) return;
    loading = true;
    refreshBtn.disabled = true;
    try {
      P.summary = await loadSummary();
      paintConditions();
      pEmit('summary');
      if (announceDone) toast(t('pl.refreshed'));
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) return;
      pEmit('summaryError', e);
      if (announceDone) toast(errorText(e, t), { error: true });
    } finally {
      loading = false;
      refreshBtn.disabled = false;
    }
  }

  function changeEvent(ev) {
    if (ev === P.event) return;
    P.event = ev;
    set({ event: ev });
    paintEvents();
    paintConditions();
    pEmit('event', ev);
    refresh();
  }

  P.changeEvent = changeEvent;
  cleanups.push(pOn('data', () => refresh()));
  cleanups.push(on('apiOk', (ok) => { if (ok) refresh(); }));
  cleanups.push(on('plannerUnauthorized', () => {
    if (demoAccess) autoKeyRejected = true;
    set({ plannerKey: null });
    toast(t('err.unauthorized'), { error: true });
    window.dispatchEvent(new Event('hashchange'));
  }));
  const timer = setInterval(() => { if (!document.hidden && !isOffline()) refresh(); }, 60000);
  cleanups.push(() => clearInterval(timer));
  cleanups.push(() => { closeDrawer(); P.root = null; });

  // Pages ----------------------------------------------------------------------
  let pageCleanup = null;
  (async () => {
    const mod = await import(`./planner-${pageName}.js`);
    pageCleanup = mod.mount(body, { refresh });
    await refresh();
  })();
  cleanups.push(() => pageCleanup?.());

  return () => cleanups.forEach((fn) => { try { fn(); } catch (e) { console.error(e); } });
}

// ── Sign-in ──────────────────────────────────────────────────────────────────
function mountLogin(root) {
  const pill = statusPill();
  const input = h('input', { type: 'password', id: 'pl-key', autocomplete: 'current-password', required: true, 'aria-describedby': 'pl-key-help' });
  const error = h('p', { class: 'err', role: 'alert', hidden: true });
  const btn = h('button', { class: 'btn primary block', type: 'submit' }, t('pl.signIn'));
  const form = h('form', { class: 'card stack', novalidate: true },
    h('h1', null, t('pl.loginTitle')),
    h('p', { class: 'muted' }, t('pl.loginHelp')),
    h('label', { class: 'field' }, t('pl.key'), input,
      h('span', { class: 'hint', id: 'pl-key-help' }, ['localhost', '127.0.0.1'].includes(location.hostname) ? t('pl.keyDemo') : t('pl.keyHelp'))),
    error, btn,
    h('a', { href: '#/', class: 'small' }, t('pl.backToResident')));

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    error.hidden = true;
    const key = input.value.trim();
    if (!key) { error.textContent = t('pl.keyRequired'); error.hidden = false; return; }
    btn.disabled = true;
    set({ plannerKey: key });
    try {
      await plannerPing();
      window.dispatchEvent(new Event('hashchange'));
    } catch (err) {
      set({ plannerKey: null });
      error.textContent = errorText(err, t);
      error.hidden = false;
      btn.disabled = false;
      input.focus();
    }
  });

  root.append(topbar({ subtitle: t('pl.subtitle'), pill }), h('main', { class: 'login', id: 'main' }, form));
  input.focus();
  return () => pill.destroy();
}
