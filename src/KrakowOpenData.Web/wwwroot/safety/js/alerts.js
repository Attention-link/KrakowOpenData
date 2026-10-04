// Planner alerts for the resident: the app asks "which alerts cover my area?" about once a minute while it is open
// and online, shows them as banners, and (if allowed) raises a device notification. The last answer is saved,
// so alerts that were already received stay visible offline until they expire.

import { h, icon, clear, toast, timeLeft } from './util.js';
import { state, set, isOffline } from './state.js';
import { t, getLang } from './i18n.js';
import { getAlerts } from './api.js';
import { kvGet, kvSet } from './db.js';
import { layerOfApi } from './model.js';

const ALERT_ICON = { Heat: 'thermo', Safety: 'moon', Flood: 'wave', Air: 'wind' };

const POLL_MS = 60000;

export function startAlerts(ctx, host, onChange) {
  let timer;
  let alerts = [];
  const api = { lastChecked: null, list: () => alerts, refresh, stop, repaint: () => paint() };

  async function refresh() {
    if (!state.me) { alerts = []; paint(); onChange?.(); return; }
    if (isOffline()) {
      const saved = await kvGet('alerts:last');
      if (saved) { alerts = saved.alerts.filter((a) => new Date(a.expiresAt) > new Date()); api.lastChecked = saved.at; }
      paint();
      return;
    }
    try {
      const fresh = await getAlerts(state.me.lat, state.me.lon);
      const seen = new Set((await kvGet('alerts:seen')) || []);
      const isNew = fresh.filter((a) => !seen.has(a.id));
      alerts = fresh;
      api.lastChecked = Date.now();
      await kvSet('alerts:last', { alerts: fresh, at: api.lastChecked });
      if (isNew.length) {
        await kvSet('alerts:seen', [...seen, ...isNew.map((a) => a.id)].slice(-100));
        isNew.forEach(notify);
      }
    } catch { /* keep what we have */ }
    paint();
    onChange?.();
  }

  function textOf(a) {
    return a.translations?.[getLang()] || a.message;
  }

  function notify(a) {
    // The toast is announced to screen readers once, for this newly seen alert; the banners themselves are not live.
    toast(t('a11y.newAlert', { title: a.title }), { ms: 8000 });
    if (state.notify && 'Notification' in window && Notification.permission === 'granted' && document.hidden) {
      try { new Notification(a.title, { body: textOf(a), tag: a.id, icon: 'icon.svg' }); } catch { /* not allowed here */ }
    }
  }

  function paint() {
    clear(host);
    // General alerts always show; heat, night-safety, flood and air alerts show in their own view.
    const mode = state.mode || 'safety';
    const relevant = (a) => !a.layer || layerOfApi(a.layer) === mode;
    const visible = alerts.filter((a) => relevant(a) && !state.dismissedAlerts.includes(a.id)).slice(0, 3);
    for (const a of visible) {
      const kind = a.severity === 'Critical' ? 'critical' : a.severity === 'Info' ? 'info' : '';
      // No role="alert"/"status" here: this is repainted on every poll and would be read out again each minute.
      host.append(h('div', { class: `banner ${kind}` },
        icon(ALERT_ICON[a.layer] || 'bell'),
        h('div', { class: 'grow' }, h('b', null, a.title), h('span', { class: 'small' }, textOf(a)),
          h('span', { class: 'tiny', style: { opacity: 0.85, display: 'block', marginTop: '.2rem' } }, `${t('alerts.from')} · ${timeLeft(a.expiresAt, t)}${isOffline() ? ' · ' + t('alerts.offline') : ''}`)),
        h('button', { class: 'btn icon quiet', type: 'button', style: { color: 'inherit', minHeight: '36px', width: '36px' }, 'aria-label': t('common.dismiss'),
          onclick: () => { set({ dismissedAlerts: [...state.dismissedAlerts, a.id] }); paint(); } }, icon('x', 'sm'))));
    }
  }

  function stop() {
    clearInterval(timer);
    document.removeEventListener('visibilitychange', onVis);
  }
  const onVis = () => { if (!document.hidden) refresh(); };
  document.addEventListener('visibilitychange', onVis);
  timer = setInterval(() => { if (!document.hidden) refresh(); }, POLL_MS);
  refresh();
  return api;
}
