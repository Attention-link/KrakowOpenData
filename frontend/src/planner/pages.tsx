// Planner pages: map, reports, alerts and contacts.

import { useEffect, useMemo, useState } from 'react';
import { Banner, Empty, Icon, Skeleton, toast } from '../components/ui';
import { LegendBody, openMethod, openScoreExplainer } from '../components/explain';
import { cancelAlert, errorText, getAgencies, getDispatches, getFeatures, getGrid, getPlannerAlerts, getReports, seedDemo } from '../lib/api';
import { useCachedQuery } from '../lib/hooks';
import { useLang, useT } from '../lib/i18n';
import { PRIORITY_RAMP, RELIEF, cellId } from '../lib/model';
import { useApp } from '../lib/store';
import { timeAgo, timeLeft } from '../lib/util';
import type { Feature, LatLon, Mode, PlannerAlert } from '../lib/types';
import { Circle, KRAKOW, MapView, Marker, Places, ScoreGrid, dotIcon, useMap } from '../map/MapView';
import { AlertForm } from './AlertForm';
import { ReportItem, StaleBanner } from './CellDrawer';
import { openAlertDialog, openDispatchComposer } from './dialogs';
import { goToMap, requireOnline, usePlanner } from './store';

// ── Map: the whole city coloured by priority or by score, with reports and alert areas on top ──
const TYPE_ICON: Record<string, string> = { LightOut: 'lamp', UnsafeAtNight: 'moon', PathHazard: 'alert', WaterNotWorking: 'water', NoShade: 'sun', HeatSpot: 'thermo' };
const SEV_COLOR = { Info: '#2a78d6', Warning: '#eda100', Critical: '#d03b3b' } as const;

function FocusOnPlace({ rows }: { rows: unknown }) {
  const map = useMap();
  const focus = usePlanner((s) => s.focus);
  const setFocus = usePlanner((s) => s.setFocus);
  const openCell = usePlanner((s) => s.openCell);
  useEffect(() => {
    if (!map || !focus) return;
    map.setView([focus.lat, focus.lon], 16);
    if (focus.cellId) openCell(focus.cellId);
    setFocus(null);
  }, [map, focus, rows]);
  return null;
}

export function PlannerMap() {
  const t = useT();
  const event = useApp((s) => s.event);
  const version = usePlanner((s) => s.dataVersion);
  const openCell = usePlanner((s) => s.openCell);
  const setGrid = usePlanner((s) => s.setGrid);
  const [metric, setMetric] = useState<'priority' | 'score'>('priority');
  const [showReports, setShowReports] = useState(true);
  const [showAlerts, setShowAlerts] = useState(true);
  const [showPlaces, setShowPlaces] = useState(false);
  const [pickForAlert, setPickForAlert] = useState(false);
  const [selected, setSelected] = useState<{ row: number; col: number } | null>(null);

  const gridQ = useCachedQuery(`p:grid:${event}`, () => getGrid(event), [version]);
  const reportsQ = useCachedQuery('p:reports', () => getReports({ includeResolved: true, limit: 500 }, true), [version]);
  const alertsQ = useCachedQuery('p:alerts', () => getPlannerAlerts(true), [version]);
  const featQ = useCachedQuery('features', getFeatures);
  const grid = gridQ.data;
  useEffect(() => { if (grid) setGrid(grid); }, [grid]);

  const mode: Mode = event === 'night' ? 'safety' : event === 'heat' ? 'heat' : 'both';
  const wantedLayer = event === 'heat' ? 'Heat' : event === 'night' ? 'Safety' : null;
  const reports = useMemo(() => (reportsQ.data || []).filter((r) => r.status === 'Open' && (!wantedLayer || r.layer === wantedLayer)), [reportsQ.data, wantedLayer]);
  const alerts = useMemo(() => (alertsQ.data || []).filter((a) => a.status === 'Active' && new Date(a.expiresAt) > new Date()), [alertsQ.data]);
  const meta = grid?.grid;

  return (
    <div className="pl-page fill">
      <MapView className="pl-map" label={t('pm.mapLabel')} center={KRAKOW} zoom={12}>
        <ScoreGrid grid={grid} mode={mode} style={metric === 'priority' ? 'priority' : 'score'} selected={selected}
          onPick={(p, c, row) => {
            if (pickForAlert) { setPickForAlert(false); openAlertDialog({ point: p }); return; }
            if (row) { setSelected(c); openCell(cellId(c.row, c.col)); }
          }} />
        <Places features={featQ.data as Feature[] | null} keys={event === 'night' ? RELIEF.safety : RELIEF[event]} enabled={showPlaces} />
        <FocusOnPlace rows={grid} />
        {showReports && reports.map((r) => (
          <Marker key={r.id} position={[r.latitude, r.longitude]} icon={dotIcon(TYPE_ICON[r.type] || 'flag', r.layer === 'Heat' ? 'heat' : 'safety', true)}
            tooltip={`${t(`rtype.${r.type}`)} · ${t('report.supporters', { n: r.supporters })}`} onClick={() => openCell(r.cellId)} />
        ))}
        {showAlerts && alerts.map((a) => (
          <Circle key={a.id} center={[a.latitude, a.longitude]} radius={a.radiusMeters} options={{ color: SEV_COLOR[a.severity], weight: 2, dashArray: '6 4', fillOpacity: 0.08 }} tooltip={`${a.title} · ${a.devicesInArea ?? 0} ${t('al.phones')}`} />
        ))}
        <div className="map-note">{gridQ.error != null && !grid ? <Banner kind="danger" icon="alert" small><span>{t('err.generic')}</span></Banner> : gridQ.stale && gridQ.savedAt ? <StaleBanner savedAt={gridQ.savedAt} /> : null}</div>
        <div className="card map-ctl">
          <div className="stack tight">
            <div className="seg" role="group" aria-label={t('pm.metric')}>
              {([['priority', t('pm.priority')], ['score', t('pm.score')]] as const).map(([m, l]) => <button key={m} type="button" aria-pressed={metric === m} onClick={() => setMetric(m)}>{l}</button>)}
            </div>
            <label className="row small"><input type="checkbox" checked={showReports} onChange={(e) => setShowReports(e.target.checked)} />{t('pm.reports')}</label>
            <label className="row small"><input type="checkbox" checked={showAlerts} onChange={(e) => setShowAlerts(e.target.checked)} />{t('pm.alerts')}</label>
            <label className="row small"><input type="checkbox" checked={showPlaces} onChange={(e) => setShowPlaces(e.target.checked)} />{`${t('map.places')} (${t('map.placesZoom')})`}</label>
            <button className={`btn sm ${pickForAlert ? 'primary' : ''}`} type="button" onClick={() => { setPickForAlert(!pickForAlert); if (!pickForAlert) toast(t('pm.pickHint')); }}><Icon name="bell" size="sm" />{pickForAlert ? t('pm.tapMap') : t('pm.newAlert')}</button>
            <div className="tiny muted">{t('pm.wholeCity')} · {t('cov.squares', { r: '250 m' })}</div>
          </div>
        </div>
        <div className="legend open">
          {metric === 'priority' ? (
            <>
              <div className="small strong">{t('pm.priorityLegend', { event: t(`event.${event}`) })}</div>
              <div className="items"><span>{t('pm.lower')}</span>{PRIORITY_RAMP.map((c) => <i key={c} style={{ background: c, width: '1.1rem' }} />)}<span>{t('pm.higher')}</span></div>
            </>
          ) : (
            <>
              <LegendBody mode={mode} meta={meta} compact onExplain={(layer) => openScoreExplainer({ layer, meta })} />
              <button className="btn sm quiet" type="button" onClick={() => openMethod(meta)}><Icon name="list" size="sm" />{t('explain.fullMethod')}</button>
            </>
          )}
        </div>
      </MapView>
    </div>
  );
}

// ── Reports: every citizen report with filters and the actions a planner needs ──
const TYPE_LAYER: Record<string, string> = { LightOut: 'Safety', UnsafeAtNight: 'Safety', PathHazard: 'Safety', WaterNotWorking: 'Heat', NoShade: 'Heat', HeatSpot: 'Heat' };
const ALL_TYPES = ['LightOut', 'UnsafeAtNight', 'PathHazard', 'WaterNotWorking', 'NoShade', 'HeatSpot'];

export function Reports() {
  const t = useT();
  const event = useApp((s) => s.event);
  const version = usePlanner((s) => s.dataVersion);
  const dataChanged = usePlanner((s) => s.dataChanged);
  const openCell = usePlanner((s) => s.openCell);
  const q = useCachedQuery('p:reports', () => getReports({ includeResolved: true, limit: 500 }, true), [version]);
  const [f, setF] = useState({ status: 'Open', type: '', verified: '', q: '' });
  const wantedLayer = event === 'heat' ? 'Heat' : event === 'night' ? 'Safety' : null;
  const shownTypes = wantedLayer ? ALL_TYPES.filter((x) => TYPE_LAYER[x] === wantedLayer) : ALL_TYPES;

  const items = (q.data || []).filter((r) => {
    if (wantedLayer && TYPE_LAYER[r.type] !== wantedLayer) return false;
    if (f.status && r.status !== f.status) return false;
    if (f.type && r.type !== f.type) return false;
    if (f.verified === 'yes' && !r.verifiedByPlanner) return false;
    if (f.verified === 'no' && r.verifiedByPlanner) return false;
    if (f.q && !`${r.note || ''} ${t(`rtype.${r.type}`)} ${r.cellId}`.toLowerCase().includes(f.q.toLowerCase())) return false;
    return true;
  });

  const seed = async () => {
    if (!requireOnline()) return;
    try { const r = await seedDemo(); toast(r.created ? t('rep.seeded', { n: r.created }) : t('rep.seedNone')); dataChanged(); }
    catch (e) { toast(errorText(e, t), { error: true }); }
  };
  const select = (key: 'status' | 'type' | 'verified', options: [string, string][]) => (
    <label className="field filter">{t(`rep.f.${key}`)}
      <select value={f[key]} onChange={(e) => setF({ ...f, [key]: e.target.value })}>{options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select>
    </label>
  );

  return (
    <div className="pl-page stack">
      <div className="row between wrap"><h1>{t('rep.title')}</h1><button className="btn sm" type="button" onClick={seed}><Icon name="plus" size="sm" />{t('rep.seed')}</button></div>
      {!q.data ? <Skeleton height={240} /> : (
        <>
          {q.stale && q.savedAt && <StaleBanner savedAt={q.savedAt} />}
          <div className="card stack tight">
            <p className="small muted"><Icon name="filter" size="sm" /> {t(wantedLayer ? `rep.showing.${event}` : 'rep.showing.both')}</p>
            <div className="row wrap bottom">
              {select('status', [['Open', t('rep.open')], ['Resolved', t('rep.resolved')], ['', t('rep.all')]])}
              {select('type', [['', t('rep.allTypes')], ...shownTypes.map((x): [string, string] => [x, t(`rtype.${x}`)])])}
              {select('verified', [['', t('rep.anyVerify')], ['yes', t('rep.verifiedOnly')], ['no', t('rep.unverifiedOnly')]])}
              <label className="field filter grow">{t('rep.f.search')}<input type="search" value={f.q} placeholder={t('rep.search')} onChange={(e) => setF({ ...f, q: e.target.value })} /></label>
            </div>
          </div>
          <div className="card">
            <p className="small muted" aria-live="polite">{t('rep.count', { n: items.length })}</p>
            {items.length === 0 ? <Empty icon="flag">{t('rep.empty')}</Empty> : (
              <ul className="list">
                {items.map((r) => (
                  <ReportItem key={r.id} r={r} onChange={() => void q.reload()}>
                    <div className="row wrap">
                      <button className="btn sm quiet" type="button" onClick={() => goToMap(r.latitude, r.longitude, r.cellId)}><Icon name="map" size="sm" />{t('rep.showMap')}</button>
                      <button className="btn sm quiet" type="button" onClick={() => openCell(r.cellId)}><Icon name="target" size="sm" />{t('rep.openArea')}</button>
                      {r.status === 'Open' && <button className="btn sm quiet" type="button" onClick={() => openAlertDialog({ point: [r.latitude, r.longitude] as LatLon })}><Icon name="bell" size="sm" />{t('cell.alert')}</button>}
                    </div>
                  </ReportItem>
                ))}
              </ul>
            )}
          </div>
          <p className="tiny muted">{t('rep.note')}</p>
        </>
      )}
    </div>
  );
}

// ── Alerts: compose, and manage the ones that are running ───────────────────
const SEV: Record<string, string> = { Info: 'accent', Warning: 'warn', Critical: 'danger' };

export function Alerts() {
  const t = useT();
  const lang = useLang();
  const version = usePlanner((s) => s.dataVersion);
  const dataChanged = usePlanner((s) => s.dataChanged);
  const q = useCachedQuery('p:alerts', () => getPlannerAlerts(true), [version]);
  const active = (q.data || []).filter((a) => a.status === 'Active' && new Date(a.expiresAt) > new Date());
  const past = (q.data || []).filter((a) => !active.includes(a));

  const cancel = async (a: PlannerAlert) => {
    if (!requireOnline()) return;
    if (!confirm(t('al.cancelConfirm', { title: a.title }))) return;
    try { await cancelAlert(a.id); toast(t('al.cancelledToast')); dataChanged(); } catch (e) { toast(errorText(e, t), { error: true }); }
  };
  const size = (m: number) => (m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${m} m`);
  const row = (a: PlannerAlert, isActive: boolean) => (
    <li key={a.id}>
      <div className="row between wrap">
        <div className="grow">
          <div className="row wrap"><b>{a.title}</b><span className={`chip ${SEV[a.severity]}`}>{t(`sev.${a.severity}`)}</span></div>
          <div className="small muted">{`${size(a.radiusMeters)} · ${a.status === 'Cancelled' ? t('al.cancelled') : isActive ? timeLeft(a.expiresAt, t) : t('time.expired')} · ${timeAgo(a.createdAt, t, lang)}`}</div>
          {isActive && <div className="small"><Icon name="users" size="sm" /> {t('al.phonesNow', { n: a.devicesInArea ?? 0 })}</div>}
          <p className="small">{a.message}</p>
        </div>
        <div className="row wrap">
          <button className="btn sm quiet" type="button" onClick={() => goToMap(a.latitude, a.longitude)}><Icon name="map" size="sm" />{t('rep.showMap')}</button>
          {isActive && <button className="btn sm danger" type="button" onClick={() => cancel(a)}>{t('al.cancel')}</button>}
        </div>
      </div>
    </li>
  );

  return (
    <div className="pl-page stack">
      <h1>{t('al.title.page')}</h1>
      <p className="muted">{t('al.pageHelp')}</p>
      <div className="grid-2 wide-left">
        <section className="card"><h2>{t('al.new')}</h2><AlertForm onSent={() => {}} /></section>
        <section className="card">
          <h2>{t('al.list')}</h2>
          {!q.data ? <Skeleton height={120} /> : (
            <>
              {q.stale && q.savedAt && <StaleBanner savedAt={q.savedAt} />}
              <p className="sect-title">{t('al.active', { n: active.length })}</p>
              {active.length ? <ul className="list">{active.map((a) => row(a, true))}</ul> : <p className="small muted">{t('al.noneActive')}</p>}
              {past.length > 0 && <details><summary className="small">{t('al.past', { n: past.length })}</summary><ul className="list">{past.slice(0, 20).map((a) => row(a, false))}</ul></details>}
            </>
          )}
        </section>
      </div>
    </div>
  );
}

// ── Contacts: the agencies a planner can reach, a prefilled brief for each, and the log of contacts made ──
export function Contacts() {
  const t = useT();
  const lang = useLang();
  const version = usePlanner((s) => s.dataVersion);
  const agQ = useCachedQuery('p:agencies', () => getAgencies(), [version]);
  const dispQ = useCachedQuery('p:dispatches', () => getDispatches(), [version]);

  return (
    <div className="pl-page stack">
      <h1>{t('ag.page')}</h1>
      <Banner kind="info" icon="info" small><span>{t('ag.simulated')}</span></Banner>
      {agQ.stale && agQ.savedAt && <StaleBanner savedAt={agQ.savedAt} />}
      {!agQ.data ? <Skeleton height={160} /> : (
        <div className="grid-2">
          {agQ.data.map((a) => (
            <section className="card" key={a.id}>
              <h2>{a.name}</h2>
              <p className="small muted">{a.responsibility}</p>
              <div className="row wrap spaced">
                {a.phone && <a className="btn sm" href={`tel:${a.phone.replace(/\s/g, '')}`}><Icon name="phone" size="sm" />{a.phone}</a>}
                {a.phone && !a.contactVerified && <span className="chip warn" title={t('ag.verifyHelp')}><Icon name="alert" size="sm" />{t('ag.verify')}</span>}
                {a.url && <a className="btn sm" href={a.url} target="_blank" rel="noopener noreferrer"><Icon name="external" size="sm" />{t('ag.website')}</a>}
              </div>
              <button className="btn primary sm" type="button" onClick={() => openDispatchComposer({ agencyId: a.id })}><Icon name="send" size="sm" />{t('ag.newContact')}</button>
            </section>
          ))}
        </div>
      )}
      <section className="card">
        <h2>{t('ag.log')}</h2>
        {!dispQ.data ? <Skeleton height={80} /> : dispQ.data.length === 0 ? <p className="small muted">{t('ag.logEmpty')}</p> : (
          <div className="tbl-wrap">
            <table className="t">
              <thead><tr>{[t('ag.when'), t('ag.agency'), t('ag.subject'), t('ag.reference'), t('ag.delivery')].map((x) => <th scope="col" key={x}>{x}</th>)}</tr></thead>
              <tbody>{dispQ.data.map((d) => (
                <tr key={d.id}><td>{timeAgo(d.createdAt, t, lang)}</td><td>{d.agencyName}</td><td>{d.subject}</td><td className="num">{d.reference}</td><td><span className="chip warn">{t(`ag.delivery.${d.delivery}`)}</span></td></tr>
              ))}</tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

