// Planner alerts for the resident: the app asks "which alerts cover my area?" about once a minute while it is open
// and online, shows them as banners, and (if allowed) raises a device notification. The last answer is saved,
// so alerts that were already received stay visible offline until they expire.

import { useCallback, useEffect, useRef, useState } from 'react';
import { getAlerts } from '../lib/api';
import { kvGet, kvSet } from '../lib/storage';
import { isOffline, useApp } from '../lib/store';
import { toast } from '../components/ui';
import type { Lang, PlannerAlert } from '../lib/types';

const POLL_MS = 60000;

export const alertText = (a: PlannerAlert, lang: Lang) => a.translations?.[lang] || a.message;

function raise(a: PlannerAlert) {
  const { lang, notify } = useApp.getState();
  toast(a.title, { ms: 8000 });
  if (notify && 'Notification' in window && Notification.permission === 'granted' && document.hidden) {
    try { new Notification(a.title, { body: alertText(a, lang), tag: a.id, icon: 'icon.svg' }); } catch { /* not allowed here */ }
  }
}

export function useAlerts() {
  const me = useApp((s) => s.me);
  const [alerts, setAlerts] = useState<PlannerAlert[]>([]);
  const [lastChecked, setLastChecked] = useState<number | null>(null);
  const live = useRef(true);

  const refresh = useCallback(async () => {
    if (!me) { setAlerts([]); return; }
    if (isOffline()) {
      const saved = await kvGet<{ alerts: PlannerAlert[]; at: number }>('alerts:last');
      if (saved && live.current) {
        setAlerts(saved.alerts.filter((a) => new Date(a.expiresAt) > new Date()));
        setLastChecked(saved.at);
      }
      return;
    }
    try {
      const fresh = await getAlerts(me.lat, me.lon);
      const seen = new Set((await kvGet<string[]>('alerts:seen')) || []);
      const isNew = fresh.filter((a) => !seen.has(a.id));
      const at = Date.now();
      await kvSet('alerts:last', { alerts: fresh, at });
      if (isNew.length) {
        await kvSet('alerts:seen', [...seen, ...isNew.map((a) => a.id)].slice(-100));
        isNew.forEach(raise);
      }
      if (live.current) { setAlerts(fresh); setLastChecked(at); }
    } catch { /* keep what we have */ }
  }, [me?.lat, me?.lon]);

  useEffect(() => {
    live.current = true;
    void refresh();
    const id = setInterval(() => { if (!document.hidden) void refresh(); }, POLL_MS);
    const vis = () => { if (!document.hidden) void refresh(); };
    document.addEventListener('visibilitychange', vis);
    return () => { live.current = false; clearInterval(id); document.removeEventListener('visibilitychange', vis); };
  }, [refresh]);

  return { alerts, lastChecked, refresh };
}

/** General alerts always show; heat and night-safety alerts show in their own view (and in Both). */
export function relevantAlerts(alerts: PlannerAlert[], mode: string, dismissed: string[]): PlannerAlert[] {
  const ok = (a: PlannerAlert) => !a.layer || mode === 'both' || (a.layer === 'Heat' && mode === 'heat') || (a.layer === 'Safety' && mode === 'safety');
  return alerts.filter((a) => ok(a) && !dismissed.includes(a.id)).slice(0, 3);
}

