// Planner dashboard shell: sign-in, navigation (side rail on wide screens, bottom bar on phones), the event selector
// (heat / night / both) and the live conditions strip. Pages render inside through the router.

import { useEffect, useRef, useState } from 'react';
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { Explainable, Icon, toast } from '../components/ui';
import { Topbar } from '../components/chrome';
import { openKpiExplainer } from '../components/explain';
import { ApiError, PLANNER_AUTO_KEY, errorText, getSummary, plannerPing } from '../lib/api';
import { useCachedQuery, useInterval } from '../lib/hooks';
import { useLang, useT } from '../lib/i18n';
import { isOffline, setApp, useApp } from '../lib/store';
import { timeAgo } from '../lib/util';
import type { EventName } from '../lib/types';
import { CellDrawer } from './CellDrawer';
import { Overview } from './Overview';
import { Alerts, Contacts, PlannerMap, Reports } from './pages';
import { changeEvent, usePlanner } from './store';

const PAGES: [string, string, string][] = [
  ['overview', 'dash', 'pl.nav.overview'],
  ['map', 'map', 'pl.nav.map'],
  ['reports', 'flag', 'pl.nav.reports'],
  ['alerts', 'bell', 'pl.nav.alerts'],
  ['contacts', 'phone', 'pl.nav.contacts']
];
const EVENTS: [EventName, string][] = [['heat', 'sun'], ['night', 'moon'], ['both', 'both']];

// The configured demo key was refused by the API: show the sign-in form instead of looping.
let autoKeyRejected = false;

export function PlannerApp() {
  const key = useApp((s) => s.plannerKey);
  // Demo access: when the host configures a demo key, the dashboard signs in with it, so the menu link opens it directly.
  useEffect(() => { if (!key && PLANNER_AUTO_KEY && !autoKeyRejected) setApp({ plannerKey: PLANNER_AUTO_KEY }); }, [key]);
  if (!key) return PLANNER_AUTO_KEY && !autoKeyRejected ? null : <Login />;
  return <Dashboard demoAccess={!!PLANNER_AUTO_KEY && key === PLANNER_AUTO_KEY} />;
}

function Dashboard({ demoAccess }: { demoAccess: boolean }) {
  const t = useT();
  const lang = useLang();
  const event = useApp((s) => s.event);
  const apiOk = useApp((s) => s.apiOk);
  const version = usePlanner((s) => s.dataVersion);
  const setSummary = usePlanner((s) => s.setSummary);
  const summary = usePlanner((s) => s.summary);
  const drawerCell = usePlanner((s) => s.drawerCell);
  const openCell = usePlanner((s) => s.openCell);
  const loc = useLocation();
  const onOverview = loc.pathname === '/planner' || loc.pathname.startsWith('/planner/overview');

  const q = useCachedQuery(`p:summary:${event}`, () => getSummary(event, 50), [version]);
  useEffect(() => { setSummary(q.data ? { data: q.data, stale: q.stale, savedAt: q.savedAt ?? Date.now() } : null); }, [q.data, q.stale, q.savedAt]);
  const setSummaryError = usePlanner((s) => s.setSummaryError);
  useEffect(() => { setSummaryError(q.data ? null : q.error, () => void q.reload()); }, [q.error, q.data]);
  // The server says the data is still being prepared (503): try again when it says to, instead of staying empty.
  const preparing = !q.data && q.error instanceof ApiError && q.error.status === 503;
  useEffect(() => {
    if (!preparing) return;
    const id = setTimeout(() => void q.reload(), ((q.error as ApiError).retryAfter || 15) * 1000);
    return () => clearTimeout(id);
  }, [preparing, q.error]);
  useInterval(() => { if (!isOffline()) void q.reload(); }, 60000);
  const wasOk = useRef(apiOk);
  useEffect(() => { if (apiOk && !wasOk.current) void q.reload(); wasOk.current = apiOk; }, [apiOk]);

  // The API refused the key: go back to the sign-in form (once, so a bad demo key does not loop).
  useEffect(() => {
    const on = () => { if (demoAccess) autoKeyRejected = true; setApp({ plannerKey: null }); toast(t('err.unauthorized'), { error: true }); };
    window.addEventListener('planner-unauthorized', on);
    return () => window.removeEventListener('planner-unauthorized', on);
  }, [demoAccess]);
  useEffect(() => () => openCell(null), []);

  const s = q.data;
  const hot = !!s && s.conditions.heat.level >= 1;
  const openReports = s?.kpis.find((k) => k.key === 'openReports')?.value || 0;
  const badges: Record<string, number> = { alerts: s?.activeAlerts || 0, reports: openReports };

  const signOut = demoAccess ? null : (
    <button className="btn sm quiet" type="button" onClick={() => { setApp({ plannerKey: null }); location.hash = '#/planner'; }}><Icon name="logout" size="sm" /><span className="hide-sm">{t('pl.signOut')}</span></button>
  );

  return (
    <>
      <Topbar subtitle={t('pl.subtitle')} savedAt={q.stale ? q.savedAt : null}
        right={<><a className="btn sm quiet" href="#/"><Icon name="users" size="sm" /><span className="hide-sm">{t('pl.residentView')}</span></a>{signOut}</>} />
      <div className="pl">
        <nav className="pl-nav" aria-label={t('pl.nav.label')}>
          {PAGES.map(([id, ic, label]) => (
            <NavLink key={id} to={`/planner/${id}`} className={({ isActive }) => (isActive ? 'active' : '')}>
              <Icon name={ic} /><span>{t(label)}</span>
              {badges[id] > 0 && <span className="badge-n">{badges[id]}</span>}
            </NavLink>
          ))}
        </nav>
        <div className="pl-col" id="main">
          <div className="pl-tools">
            {!onOverview && <a className="btn sm" href="#/planner/overview"><Icon name="left" size="sm" />{t('nav.backDashboard')}</a>}
            <span className="small muted hide-sm">{t('pl.planningFor')}</span>
            <div className="seg" role="group" aria-label={t('pl.event')}>
              {EVENTS.map(([ev, ic]) => <button key={ev} type="button" data-mode={ev === 'night' ? 'safety' : ev} aria-pressed={event === ev} onClick={() => changeEvent(ev)}><Icon name={ic} size="sm" />{t(`event.${ev}`)}</button>)}
            </div>
            <div className="row wrap grow">
              {s && event !== 'night' && (
                <Explainable onOpen={() => openKpiExplainer('temperature', {
                  value: s.conditions.heat.temperatureC !== null ? `${s.conditions.heat.temperatureC.toFixed(1)} °C` : '–',
                  extra: [[t('cond.heat'), t(`heat.${s.conditions.heat.pressure}`)], s.conditions.heat.warningTitle ? [t('cond.warningsTitle'), s.conditions.heat.warningTitle] : null,
                    [t('cond.airTitle'), s.conditions.air.pm25 !== null ? `${t(`air.${s.conditions.air.band}`)} · PM2.5 ${s.conditions.air.pm25} µg/m³ (${s.conditions.air.station || ''})` : t('air.Unknown')]]
                })} hint={t('cond.temp.title')}>
                  <span className={`chip ${hot ? 'warn' : 'heat'}`}><Icon name="thermo" size="sm" />{s.conditions.heat.temperatureC !== null ? `${Math.round(s.conditions.heat.temperatureC)}°C · ` : ''}{t(`heat.${s.conditions.heat.pressure}`)}</span>
                </Explainable>
              )}
              {s && event !== 'heat' && (
                <Explainable onOpen={() => openKpiExplainer('daylight', { value: `${s.conditions.sunriseLocal ?? '–'} → ${s.conditions.sunsetLocal ?? '–'}`, extra: [[t('cond.nowDark'), s.conditions.isDark ? '✓' : '–']] })} hint={t('cond.daylight.title')}>
                  <span className={`chip ${s.conditions.isDark ? 'safety' : ''}`}><Icon name={s.conditions.isDark ? 'moon' : 'sun'} size="sm" />{s.conditions.isDark ? t('cond.dark', { time: s.conditions.sunriseLocal || '' }) : t('cond.light', { time: s.conditions.sunsetLocal || '' })}</span>
                </Explainable>
              )}
              {s?.conditions.dataGaps?.length ? <span className="chip warn" title={s.conditions.dataGaps.join(', ')}><Icon name="alert" size="sm" />{t('pl.incomplete')}</span> : null}
              {demoAccess && <span className="chip" title={t('pl.demoAccess')}><Icon name="info" size="sm" />{t('pl.demoChip')}</span>}
            </div>
            {summary && <span className="tiny muted">{summary.stale ? t('pl.savedAt', { when: timeAgo(summary.savedAt, t, lang) }) : t('pl.updated', { when: timeAgo(summary.savedAt, t, lang) })}</span>}
            <button className="btn sm quiet" type="button" aria-label={t('pl.refresh')} disabled={q.loading} onClick={() => { void q.reload(); toast(t('pl.refreshed')); }}><Icon name="refresh" size="sm" /></button>
          </div>
          <div className="pl-body">
            <Routes>
              <Route path="overview" element={<Overview />} />
              <Route path="map" element={<PlannerMap />} />
              <Route path="reports" element={<Reports />} />
              <Route path="alerts" element={<Alerts />} />
              <Route path="contacts" element={<Contacts />} />
              <Route path="*" element={<Navigate to="/planner/overview" replace />} />
            </Routes>
          </div>
          {drawerCell && <CellDrawer cellId={drawerCell} />}
        </div>
      </div>
    </>
  );
}

// ── Sign-in ──────────────────────────────────────────────────────────────────
function Login() {
  const t = useT();
  const [value, setValue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const local = ['localhost', '127.0.0.1'].includes(location.hostname);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    const k = value.trim();
    if (!k) { setError(t('pl.keyRequired')); return; }
    setBusy(true);
    setApp({ plannerKey: k });
    try { await plannerPing(); } catch (err) {
      setApp({ plannerKey: null });
      setError(errorText(err, t));
      setBusy(false);
    }
  };

  return (
    <>
      <Topbar subtitle={t('pl.subtitle')} />
      <main className="login" id="main">
        <form className="card stack" noValidate onSubmit={submit}>
          <h1>{t('pl.loginTitle')}</h1>
          <p className="muted">{t('pl.loginHelp')}</p>
          <label className="field">{t('pl.key')}
            <input type="password" autoComplete="current-password" required autoFocus value={value} onChange={(e) => setValue(e.target.value)} aria-describedby="pl-key-help" />
            <span className="hint" id="pl-key-help">{local ? t('pl.keyDemo') : t('pl.keyHelp')}</span>
          </label>
          {error && <p className="err" role="alert">{error}</p>}
          <button className="btn primary block" type="submit" disabled={busy}>{t('pl.signIn')}</button>
          <a href="#/" className="small">{t('pl.backToResident')}</a>
        </form>
      </main>
    </>
  );
}

