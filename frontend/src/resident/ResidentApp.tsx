// Resident view: a full-screen map coloured by the chosen view (night safety, heat or both), a panel with the place card,
// reports and the walk check. Each view shows only its own data. Phones get a bottom sheet; wide screens a side panel.

import L from 'leaflet';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Banner, Icon, openDialog, toast } from '../components/ui';
import { LegendBody, loadMethod, openMethod, openScoreExplainer, bandText } from '../components/explain';
import { Topbar, openHowItWorks } from '../components/chrome';
import { AddressLine } from '../lib/geo';
import { ApiError, cachedGet, errorText, getConditions, getFeatures, getGrid, getPlace, getReportTypes } from '../lib/api';
import { useCachedQuery, useInterval, useMedia } from '../lib/hooks';
import { t, useT } from '../lib/i18n';
import { RELIEF, bandOf, cellOf, indexGrid, kindOf, scoreOf } from '../lib/model';
import { eventForMode, isOffline, setApp, useApp, useOffline } from '../lib/store';
import { formatDistance, timeAgo } from '../lib/util';
import type { Conditions, LatLon, Mode } from '../lib/types';
import { Circle, KRAKOW, MapCtx, MapView, Marker, Places, ScoreGrid, dotIcon, pinIcon } from '../map/MapView';
import { ResidentContext, layersOf, type ResidentCtx, type Selected, type Sheet, type View, type ViewProps } from './context';
import { HomeView } from './HomeView';
import { PlaceView } from './PlaceView';
import { ReportView } from './ReportView';
import { WalkView } from './WalkView';
import { relevantAlerts, useAlerts, alertText } from './useAlerts';

const MODES: [Mode, string][] = [['safety', 'moon'], ['heat', 'sun'], ['both', 'both']];


export function ResidentApp() {
  const t = useT();
  const lang = useApp((s) => s.lang);
  const storedMode = useApp((s) => s.mode);
  const me = useApp((s) => s.me);
  const welcomed = useApp((s) => s.welcomed);
  const dismissed = useApp((s) => s.dismissedAlerts);
  const apiOk = useApp((s) => s.apiOk);
  const offline = useOffline();
  const mode: Mode = storedMode || 'both';

  const [map, setMap] = useState<L.Map | null>(null);
  const [view, setView] = useState<View>('home');
  const [viewProps, setViewProps] = useState<ViewProps | null>(null);
  const [sheet, setSheetState] = useState<Sheet>(() => (matchMedia('(max-width: 959.98px)').matches ? 'peek' : 'half'));
  const [selected, setSelected] = useState<Selected | null>(null);
  const [placesOn, setPlacesOn] = useState(true);
  const [legendOpen, setLegendOpen] = useState(false);
  const clickHandler = useRef<((p: LatLon) => boolean) | null>(null);
  const selToken = useRef(0);
  const mainRef = useRef<HTMLDivElement>(null);
  const isPhone = useMedia('(max-width: 959.98px)');

  // ── Data ─────────────────────────────────────────────────────────────────
  const gridQ = useCachedQuery('grid:both', () => getGrid('both'));
  const condQ = useCachedQuery('conditions', getConditions);
  const featQ = useCachedQuery('features', getFeatures);
  const typesQ = useCachedQuery('report-types', getReportTypes);
  const alertsApi = useAlerts();

  const grid = gridQ.data;
  const gridIndex = useMemo(() => (grid ? indexGrid(grid) : new Map<string, number[]>()), [grid]);
  const conditions: Conditions | null = condQ.data;

  // The grid is still being prepared on the server: try again when it says so.
  const preparing = gridQ.error instanceof ApiError && gridQ.error.status === 503;
  useEffect(() => {
    if (!preparing) return;
    const id = setTimeout(() => void gridQ.reload(), ((gridQ.error as ApiError).retryAfter || 20) * 1000);
    return () => clearTimeout(id);
  }, [preparing, gridQ.error]);

  // Refresh every 5 minutes, and when the API answers again.
  useInterval(() => { if (!isOffline()) { void gridQ.reload(); void condQ.reload(); } }, 5 * 60 * 1000);
  // (Only on the change from "not answering" to "answering": the first load is started by the queries themselves.)
  const wasOk = useRef(apiOk);
  useEffect(() => {
    if (apiOk && !wasOk.current) { void gridQ.reload(); void condQ.reload(); void featQ.reload(); void alertsApi.refresh(); }
    wasOk.current = apiOk;
  }, [apiOk]);

  // The first time, the conditions choose the view (dark: night safety, hot: heat).
  useEffect(() => { if (!storedMode && conditions) setApp({ mode: conditions.suggestedMode || 'both' }); }, [conditions, storedMode]);
  useEffect(() => { void loadMethod().catch(() => {}); }, []);

  // ── Sheet ────────────────────────────────────────────────────────────────
  const setSheet = useCallback((s: Sheet) => setSheetState(s), []);
  useEffect(() => { if (mainRef.current) mainRef.current.dataset.sheet = sheet; }, [sheet]);

  // ── Selecting a place ────────────────────────────────────────────────────
  const selectPoint: ResidentCtx['selectPoint'] = useCallback((lat, lon, { fly = false, keepView = false, label = null } = {}) => {
    const token = ++selToken.current;
    const c = grid ? cellOf(grid.grid, lat, lon) : null;
    setSelected({ lat, lon, row: c?.row, col: c?.col, label, place: null, loading: true, error: null, stale: false, savedAt: null });
    if (!keepView) {
      setView('place');
      setViewProps(null);
      setSheetState('half');
      // On phones the sheet covers the lower half of the map: pan so the selected spot stays visible above it.
      if (matchMedia('(max-width: 959.98px)').matches && map) setTimeout(() => revealPoint(map, lat, lon, mainRef.current), 280);
    }
    if (fly && map) map.flyToBounds(L.latLng(lat, lon).toBounds((grid?.grid.searchRadiusMeters || 1500) * 2), { maxZoom: 15, duration: 0.6 });

    const event = eventForMode(useApp.getState().mode || 'both');
    const key = `place:${c ? `${c.row}-${c.col}` : `${lat.toFixed(3)},${lon.toFixed(3)}`}:${event}`;
    cachedGet(key, () => getPlace(lat, lon, event)).then(
      ({ data, stale, savedAt }) => { if (token === selToken.current) setSelected((s) => (s ? { ...s, place: data, stale, savedAt, loading: false } : s)); },
      (error) => { if (token === selToken.current) setSelected((s) => (s ? { ...s, loading: false, error } : s)); });
  }, [grid, map]);

  const clearSelection = useCallback(() => { selToken.current++; setSelected(null); }, []);

  const showView: ResidentCtx['showView'] = useCallback((v, props = null) => {
    setView(v);
    setViewProps(props);
    if (v === 'home') clearSelection();
  }, [clearSelection]);

  // The selected place is read again for the new view, so the card never shows data from the other one.
  useEffect(() => {
    if (selected && view === 'place') selectPoint(selected.lat, selected.lon, { keepView: true, label: selected.label });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode]);

  // After the grid arrives the selected square can be outlined.
  const selectedCell = selected && grid ? cellOf(grid.grid, selected.lat, selected.lon) : null;

  // ── Locate ───────────────────────────────────────────────────────────────
  const [locating, setLocating] = useState(false);
  const locateMe = useCallback((asArea = false) => {
    if (!('geolocation' in navigator)) { toast(t('locate.unsupported'), { error: true }); return; }
    setLocating(true);
    navigator.geolocation.getCurrentPosition((pos) => {
      setLocating(false);
      const { latitude: lat, longitude: lon } = pos.coords;
      if (lat < 49.9 || lat > 50.2 || lon < 19.7 || lon > 20.3) { toast(t('locate.outside'), { error: true }); return; }
      setApp({ me: { lat, lon, source: 'gps' } });
      selectPoint(lat, lon, { fly: true });
      if (asArea) toast(t('place.areaSet'));
    }, (err) => {
      setLocating(false);
      toast(err.code === 1 ? t('locate.denied') : t('locate.failed'), { error: true });
    }, { enableHighAccuracy: true, timeout: 12000, maximumAge: 60000 });
  }, [selectPoint, lang]);

  // ── Welcome ──────────────────────────────────────────────────────────────
  useEffect(() => { if (!welcomed && gridQ.data) welcome(); }, [gridQ.data === null]);

  // ── Map clicks: a view may claim them (picking a point), otherwise the place is selected ──
  const onMapPick = (p: LatLon) => {
    if (clickHandler.current?.(p)) return;
    if (view === 'report' || view === 'walk') return;   // those views only react to a tap when they asked for one
    selectPoint(p[0], p[1]);
  };

  const ctx: ResidentCtx | null = map ? {
    map, mode, grid, gridIndex, features: featQ.data, reportTypes: typesQ.data, selected, view, viewProps, showView, selectPoint,
    refreshGrid: () => void gridQ.reload(), setSheet, registerMapClick: (fn) => { clickHandler.current = fn; },
    locateMe, refreshAlerts: () => void alertsApi.refresh(), alertsLastChecked: alertsApi.lastChecked
  } : null;

  const visibleAlerts = relevantAlerts(alertsApi.alerts, mode, dismissed);
  const savedAt = gridQ.stale ? gridQ.savedAt : null;
  const radius = grid?.grid.searchRadiusMeters || 1500;
  const meta = grid?.grid;

  return (
    <>
      <Topbar savedAt={savedAt} />
      <div className="res-bar">
        <span className="small muted hide-sm">{t('mode.label')}</span>
        <div className="seg" role="group" aria-label={t('mode.label')}>
          {MODES.map(([m, ic]) => (
            <button key={m} type="button" data-mode={m} aria-pressed={mode === m} onClick={() => { setApp({ mode: m }); }}><Icon name={ic} size="sm" />{t(`mode.${m}`)}</button>
          ))}
        </div>
        <button className="btn sm quiet" type="button" onClick={openHowItWorks}><Icon name="info" size="sm" /><span>{t('how.link')}</span></button>
      </div>
      {conditions && <ConditionsStrip c={conditions} stale={condQ.stale} mode={mode} onOpen={() => openConditions(conditions, mode)} />}

      <div className="res-main" id="main" ref={mainRef} data-sheet={sheet}>
        <MapView className="res-map" label={t('map.label')} center={me ? [me.lat, me.lon] : KRAKOW} zoom={me ? 15 : 13} onReady={setMap}>
          <ScoreGrid grid={grid} mode={mode} selected={selectedCell} onPick={(p) => onMapPick(p)}
            hoverText={(row) => {
              const score = scoreOf(row, mode);
              const kind = kindOf(mode);
              return `${t(`mode.${mode}.score`)}: ${score} · ${bandText(bandOf(score, meta, kind), kind)} (${t(mode === 'heat' ? 'explain.dir.heat' : mode === 'safety' ? 'explain.dir.safety' : 'explain.dir.both')})`;
            }} />
          <Places features={featQ.data} keys={RELIEF[mode]} enabled={placesOn} onWalk={(f) => showView('walk', { to: [f.latitude, f.longitude], toLabel: f.name || t(`kind.${f.kind}`) })} />
          {me && <Marker position={[me.lat, me.lon]} icon={dotIcon(null, 'me')} title={t('home.myArea')} />}
          {selected && view !== 'walk' && view !== 'report' && (
            <>
              <Marker position={[selected.lat, selected.lon]} icon={pinIcon()} z={1000} />
              <Circle center={[selected.lat, selected.lon]} radius={radius} options={{ color: '#17171a', weight: 1.5, opacity: 0.7, dashArray: '6 6', fillColor: '#17171a', fillOpacity: 0.03, interactive: false }}
                tooltip={t('cov.circle', { r: formatDistance(radius) })} />
            </>
          )}
          <MapOverlay selected={selected} view={view} radius={radius}
            alerts={visibleAlerts.map((a) => ({ a, text: alertText(a, lang) }))}
            note={preparing ? <Banner icon="refresh" small>{t('map.preparing')}</Banner>
              : gridQ.error && !grid ? <Banner kind="danger" icon="alert" small><span>{errorText(gridQ.error, t)}</span> <button className="btn sm" type="button" onClick={() => void gridQ.reload()}>{t('common.retry')}</button></Banner>
              : gridQ.stale && savedAt ? <Banner kind="warn" icon="offline" small>{t('map.savedData', { when: timeAgo(savedAt, t, lang) })}</Banner> : null}
            legendOpen={legendOpen} setLegendOpen={setLegendOpen} placesOn={placesOn} setPlacesOn={setPlacesOn}
            locate={() => locateMe()} locating={locating} legend={
              <>
                <LegendBody mode={mode} meta={meta} compact onExplain={(layer) => openScoreExplainer({ layer, meta })} />
                <div className="tiny muted" style={{ marginTop: '.3rem' }}>{t('legend.note')}</div>
                <button className="btn sm quiet" type="button" onClick={() => openMethod(meta)}><Icon name="list" size="sm" />{t('explain.fullMethod')}</button>
              </>
            } />
        </MapView>

        {ctx && (
          <MapCtx.Provider value={map}>
          <ResidentContext.Provider value={ctx}>
            <Panel sheet={sheet} setSheet={setSheet} mainRef={mainRef} view={view} showView={showView} isPhone={isPhone}>
              {view === 'home' && <HomeView conditions={conditions} />}
              {view === 'place' && <PlaceView />}
              {view === 'report' && <ReportView />}
              {view === 'walk' && <WalkView />}
            </Panel>
          </ResidentContext.Provider>
          </MapCtx.Provider>
        )}
      </div>
    </>
  );
}

/** Pans the map so a point sits in the middle of the part of the map the bottom sheet does not cover. */
function revealPoint(map: L.Map, lat: number, lon: number, main: HTMLElement | null) {
  const panel = main?.querySelector('.panel') as HTMLElement | null;
  if (!panel) return;
  const visible = map.getSize().y - panel.offsetHeight;
  if (visible < 120) return;
  const pt = map.latLngToContainerPoint([lat, lon]);
  const target = visible / 2 + 40;
  if (pt.y > visible - 30 || pt.y < 40) map.panBy([0, pt.y - target], { animate: true });
}

// ── Overlay: address, alerts, notes, buttons, legend ─────────────────────────
interface OverlayProps {
  selected: Selected | null;
  view: View;
  radius: number;
  alerts: { a: ReturnType<typeof relevantAlerts>[number]; text: string }[];
  note: React.ReactNode;
  legendOpen: boolean;
  setLegendOpen: (v: boolean) => void;
  placesOn: boolean;
  setPlacesOn: (v: boolean) => void;
  locate: () => void;
  locating: boolean;
  legend: React.ReactNode;
}

function MapOverlay({ selected, view, radius, alerts, note, legendOpen, setLegendOpen, placesOn, setPlacesOn, locate, locating, legend }: OverlayProps) {
  const t = useT();
  const dismissed = useApp((s) => s.dismissedAlerts);
  return (
    <>
      <div className="map-top">
        {selected && (view === 'place' || view === 'home') && (
          <div className="map-info" role="status">
            <AddressLine lat={selected.lat} lon={selected.lon} fallback={selected.label} />
            <div className="tiny"><Icon name="target" size="sm" /> {t('cov.radius', { r: formatDistance(radius) })}</div>
          </div>
        )}
        <div className="alert-stack" aria-live="polite">
          {alerts.map(({ a, text }) => (
            <div key={a.id} className={`banner ${a.severity === 'Critical' ? 'critical' : a.severity === 'Info' ? 'info' : ''}`} role={a.severity === 'Critical' ? 'alert' : 'status'}>
              <Icon name={a.layer === 'Heat' ? 'thermo' : a.layer === 'Safety' ? 'moon' : 'bell'} />
              <div className="grow"><b>{a.title}</b><span className="small block">{text}</span></div>
              <button className="btn icon quiet" type="button" aria-label={t('common.dismiss')} onClick={() => setApp({ dismissedAlerts: [...dismissed, a.id] })}><Icon name="x" size="sm" /></button>
            </div>
          ))}
        </div>
      </div>
      <div className="map-note">{note}</div>
      <div className="map-btns top">
        <button className="btn icon" type="button" aria-label={t('map.locate')} title={t('map.locate')} disabled={locating} onClick={locate}><Icon name="locate" /></button>
        <button className="btn icon" type="button" aria-pressed={placesOn} aria-label={t('map.places')} title={`${t('map.places')} (${t('map.placesZoom')})`} onClick={() => setPlacesOn(!placesOn)}><Icon name="building" /></button>
        <button className="btn icon" type="button" aria-pressed={legendOpen} aria-label={t('map.legend')} title={t('map.legend')} onClick={() => setLegendOpen(!legendOpen)}><Icon name="layers" /></button>
      </div>
      <div className={`legend ${legendOpen ? 'open' : ''}`}>{legend}</div>
    </>
  );
}

// ── Panel: side panel on wide screens, draggable bottom sheet on phones ─────
interface PanelProps {
  sheet: Sheet;
  setSheet: (s: Sheet) => void;
  mainRef: React.RefObject<HTMLDivElement>;
  view: View;
  showView: ResidentCtx['showView'];
  isPhone: boolean;
  children: React.ReactNode;
}

const STATES: Sheet[] = ['peek', 'half', 'full'];

function Panel({ sheet, setSheet, mainRef, view, showView, children }: PanelProps) {
  const t = useT();
  const titleRef = useRef<HTMLHeadingElement>(null);
  const panelRef = useRef<HTMLElement>(null);
  const bodyRef = useRef<HTMLDivElement>(null);
  const drag = useRef<{ y: number; h: number; moved: boolean } | null>(null);
  const mode = useApp((s) => s.mode) || 'both';
  const cycle = () => setSheet(STATES[(STATES.indexOf(sheet) + 1) % 3]);

  const title = view === 'home' ? t('home.title') : view === 'place' ? t('place.title') : view === 'report' ? t('report.title') : t(`walk.title.${mode}`);

  // Moving to another view: scroll to the top and move focus to the panel title.
  useEffect(() => { if (bodyRef.current) bodyRef.current.scrollTop = 0; titleRef.current?.focus({ preventScroll: true }); }, [view]);

  const down = (e: React.PointerEvent) => {
    drag.current = { y: e.clientY, h: panelRef.current!.offsetHeight, moved: false };
    panelRef.current!.classList.add('dragging');
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  };
  const move = (e: React.PointerEvent) => {
    const d = drag.current;
    if (!d) return;
    if (Math.abs(e.clientY - d.y) > 5) d.moved = true;
    const max = mainRef.current!.offsetHeight - 8;
    mainRef.current!.style.setProperty('--sheet-h', `${Math.min(max, Math.max(90, d.h + (d.y - e.clientY)))}px`);
  };
  const end = () => {
    const d = drag.current;
    if (!d) return;
    panelRef.current!.classList.remove('dragging');
    const hgt = panelRef.current!.offsetHeight;
    const max = mainRef.current!.offsetHeight - 8;
    mainRef.current!.style.removeProperty('--sheet-h');
    if (!d.moved) cycle();
    else {
      const targets: Record<Sheet, number> = { peek: 120, half: max * 0.52, full: max };
      setSheet((Object.entries(targets) as [Sheet, number][]).sort((a, b) => Math.abs(a[1] - hgt) - Math.abs(b[1] - hgt))[0][0]);
    }
    drag.current = null;
  };

  return (
    <section className="panel" ref={panelRef} data-state={sheet} aria-labelledby="panel-title">
      <button className="handle" type="button" aria-label={t('sheet.toggle')} onPointerDown={down} onPointerMove={move} onPointerUp={end} onPointerCancel={end}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); cycle(); } }} />
      <div className="panel-head">
        {view !== 'home' && <button className="btn sm" type="button" onClick={() => showView('home')}><Icon name="left" size="sm" />{t('nav.backMenu')}</button>}
        <h2 id="panel-title" tabIndex={-1} ref={titleRef}>{title}</h2>
      </div>
      <div className="panel-body" ref={bodyRef}>{children}</div>
    </section>
  );
}

// ── Conditions strip and dialog (only what matters for the chosen view) ─────
function ConditionsStrip({ c, stale, mode, onOpen }: { c: Conditions; stale: boolean; mode: Mode; onOpen: () => void }) {
  const t = useT();
  const chip = (kind: string, ic: string, content: React.ReactNode, key: string) => (
    <button key={key} className={`chip ${kind}`} type="button" onClick={onOpen}><Icon name={ic} size="sm" /><span>{content}</span></button>
  );
  const chips: React.ReactNode[] = [];
  if (mode !== 'safety') {
    chips.push(chip(c.heat.level >= 1 ? 'warn' : 'heat', 'thermo', <>{c.heat.temperatureC !== null ? `${Math.round(c.heat.temperatureC)}°C` : '–'} · {t(`heat.${c.heat.pressure}`)}</>, 'heat'));
    if (c.air.band && c.air.band !== 'Unknown') chips.push(chip(c.air.band === 'Poor' || c.air.band === 'VeryPoor' ? 'danger' : c.air.band === 'Good' ? 'ok' : '', 'wind', t('cond.air', { band: t(`air.${c.air.band}`) }), 'air'));
  }
  if (mode !== 'heat') chips.push(chip(c.isDark ? 'safety' : '', c.isDark ? 'moon' : 'sun', c.isDark ? t('cond.dark', { time: c.sunriseLocal || '' }) : t('cond.light', { time: c.sunsetLocal || '' }), 'dark'));
  if (mode === 'both') {
    if (c.hydro.elevatedGauges > 0) chips.push(chip('danger', 'wave', t('cond.rivers', { n: c.hydro.elevatedGauges }), 'rivers'));
    const w = (c.warnings || []).filter((x) => x.level > 0);
    if (w.length) chips.push(chip('warn', 'alert', t('cond.warnings', { n: w.length }), 'warn'));
  }
  if (stale) chips.push(chip('warn', 'offline', t('cond.saved'), 'saved'));
  return <div className="cond-strip" aria-label={t('cond.title')}>{chips}</div>;
}

function openConditions(c: Conditions, mode: Mode) {
  {
    const warnings = mode === 'safety' ? [] : (c.warnings || []).filter((w) => w.level > 0);
    const rows: [string, string][] = [];
    if (mode !== 'safety') {
      rows.push([t('cond.heat'), `${t(`heat.${c.heat.pressure}`)}${c.heat.temperatureC !== null ? ` (${c.heat.temperatureC.toFixed(1)} °C)` : ''}`]);
      rows.push([t('cond.airTitle'), c.air.pm25 !== null ? `${t(`air.${c.air.band}`)} · PM2.5 ${c.air.pm25} µg/m³` : t('air.Unknown')]);
    }
    if (mode !== 'heat') rows.push([t('cond.daylight'), `${c.sunriseLocal ?? '–'} → ${c.sunsetLocal ?? '–'} ${c.isDark ? '· ' + t('cond.nowDark') : ''}`]);
    if (mode === 'both') rows.push([t('cond.riversTitle'), c.hydro.elevatedGauges ? t('cond.rivers', { n: c.hydro.elevatedGauges }) : t('cond.riversOk')]);
    openDialog((close) => ({
      title: t('cond.title'),
      body: (
        <div className="stack">
          <dl className="kv">{rows.map(([k, v]) => <div className="kv-row" key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl>
          {warnings.length
            ? <div><p className="sect-title">{t('cond.warningsTitle')}</p><ul className="list">{warnings.map((w, i) => <li key={i}><b>{w.eventName} · {t('cond.level', { n: w.level })}</b><p className="small muted">{(w.content || '').slice(0, 220)}</p></li>)}</ul></div>
            : mode !== 'safety' ? <p className="small muted">{t('cond.noWarnings')}</p> : null}
          <p className="tiny muted">{t('cond.source')}</p>
        </div>
      ),
      footer: <button className="btn primary" type="button" onClick={() => close('ok')}>{t('common.done')}</button>
    }));
  }
}

// ── Welcome ──────────────────────────────────────────────────────────────────
let welcomeOpen = false;
function welcome() {
  if (welcomeOpen) return;   // (StrictMode runs effects twice in development)
  welcomeOpen = true;
  openDialog((close) => ({
    title: t('welcome.title'),
    body: (
      <div className="stack">
        <p>{t('welcome.p1')}</p>
        <ul className="list">
          <li className="row"><Icon name="moon" /><span>{t('welcome.safety')}</span></li>
          <li className="row"><Icon name="sun" /><span>{t('welcome.heat')}</span></li>
          <li className="row"><Icon name="flag" /><span>{t('welcome.report')}</span></li>
          <li className="row"><Icon name="offline" /><span>{t('welcome.offline')}</span></li>
        </ul>
        <p className="banner info small"><Icon name="info" /><span>{t('how.notCrime')}</span></p>
      </div>
    ),
    footer: <button className="btn primary" type="button" onClick={() => close('ok')}>{t('welcome.start')}</button>
  }), { onClose: () => { welcomeOpen = false; setApp({ welcomed: true }); } });
}
