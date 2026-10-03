// Walk check along real streets. Night safety = a night walk (lighting, night transport, open places, reports);
// Heat = a cool walk (shade, water, cool places, reports); Both = the weaker of the two.
// Pick the start (A) and the destination (B) by typing an address or stop, tapping the map, or using your area.
// The API finds the fastest street route and, when one scores clearly better, a safer / cooler / better one to compare with it.
// Offline (or when street routing is down) the straight line between the two points is estimated from the saved map.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Banner, BandBadge, Icon, InfoButton, Skeleton } from '../components/ui';
import { SearchBox } from '../components/chrome';
import { bandText, openScoreExplainer } from '../components/explain';
import { AddressLine, reverseLabel } from '../lib/geo';
import { errorText, getRoutes } from '../lib/api';
import { useT } from '../lib/i18n';
import { BAND_FILL, COL, FACTOR_ICON, bandOf, cellOf, kindOf, nearestFromFeatures } from '../lib/model';
import { eventForMode, isOffline, useApp } from '../lib/store';
import { formatDistance, haversine } from '../lib/util';
import type { Band, CorridorSample, LatLon, Mode, RouteOption, Routes } from '../lib/types';
import { FitBounds, Marker, Polyline, dotIcon, endIcon } from '../map/MapView';
import { bandRuns, SCORE_KEY } from '../lib/route';
import { useResident } from './context';

const HELP_KEYS: Record<Mode, string[]> = { safety: ['openPlaces', 'aed'], heat: ['water', 'green', 'refuge', 'toilets'], both: ['water', 'green', 'openPlaces'] };

type End = 'from' | 'to';

export function WalkView() {
  const t = useT();
  const ctx = useResident();
  const me = useApp((s) => s.me);
  const { mode, grid, gridIndex, features } = ctx;
  const key = SCORE_KEY[mode];
  const kind = kindOf(mode);
  const meta = grid?.grid;

  const [from, setFrom] = useState<LatLon | null>(() => (me ? [me.lat, me.lon] : null));
  const [to, setTo] = useState<LatLon | null>(() => ctx.viewProps?.to ?? null);
  const [labels, setLabels] = useState<Record<End, string | null>>({ from: null, to: ctx.viewProps?.toLabel ?? null });
  const [pick, setPick] = useState<End | null>(null);
  const [routes, setRoutes] = useState<Routes | null>(null);
  const [selectedKind, setSelectedKind] = useState<'fastest' | 'better'>('fastest');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [offlineApprox, setOfflineApprox] = useState(false);
  const [fitKey, setFitKey] = useState(0);
  const token = useRef(0);

  // Picking a point on the map claims the next map click.
  useEffect(() => {
    if (!pick) { ctx.registerMapClick(null); return; }
    ctx.registerMapClick((p) => { setEnd(pick, p, null); setPick(null); return true; });
    return () => ctx.registerMapClick(null);
  }, [pick]);

  const setEnd = useCallback((which: End, ll: LatLon, label: string | null) => {
    (which === 'from' ? setFrom : setTo)(ll);
    setLabels((l) => ({ ...l, [which]: label }));
    if (!label) void reverseLabel(ll[0], ll[1]).then((found) => setLabels((l) => (l[which] === null ? { ...l, [which]: found } : l)));
  }, []);

  /** The same idea from the saved grid: a straight line, 50 m samples, the scores of the cell each sample falls in. */
  const approximate = useCallback((a: LatLon, b: LatLon): Routes | null => {
    if (!grid || !gridIndex.size) return null;
    const length = haversine(a, b);
    if (length > 5000) return null;
    const steps = Math.max(1, Math.ceil(length / 50));
    const samples: CorridorSample[] = [];
    for (let i = 0; i <= steps; i++) {
      const f = i / steps;
      const lat = a[0] + (b[0] - a[0]) * f, lon = a[1] + (b[1] - a[1]) * f;
      const c = cellOf(grid.grid, lat, lon);
      const row = gridIndex.get(`${c.row}-${c.col}`);
      samples.push({ latitude: lat, longitude: lon, safety: row ? row[COL.safety] : 0, heat: row ? row[COL.heat] : 100, combined: row ? row[COL.combined] : 0 });
    }
    const vals = samples.map((s) => s[key]);
    const worstIdx = vals.indexOf(kind === 'heat' ? Math.max(...vals) : Math.min(...vals));
    const fastest: RouteOption = {
      kind: 'fastest', lengthMeters: Math.round(length), walkingMinutes: Math.ceil(length / 80), path: [a, b], samples,
      average: vals.reduce((x, v) => x + v, 0) / vals.length, worst: vals[worstIdx], weakestSampleIndex: worstIdx, openReportsNearby: 0
    };
    return { mode: eventForMode(mode), source: 'straight-line', fastest, better: null, betterKind: 'none', scoreGain: 0, extraMeters: 0, extraMinutes: 0, note: '' };
  }, [grid, gridIndex, key, kind, mode]);

  // Compute the routes whenever the two ends or the view change.
  useEffect(() => {
    if (!from || !to) { setRoutes(null); return; }
    const mine = ++token.current;
    setLoading(true); setError(null); setRoutes(null); setOfflineApprox(false);
    (async () => {
      try {
        if (isOffline()) throw new Error('offline');
        const r = await getRoutes(from, to, eventForMode(mode));
        if (mine !== token.current) return;
        setRoutes(r);
        setSelectedKind(r.better ? 'better' : 'fastest');
      } catch (e: any) {
        if (mine !== token.current) return;
        const approx = approximate(from, to);
        if (approx) { setRoutes(approx); setOfflineApprox(true); setSelectedKind('fastest'); }
        else setError(e?.message === 'offline' ? t('err.offline') : errorText(e, t));
      }
      if (mine !== token.current) return;
      setLoading(false);
      setFitKey((k) => k + 1);
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from?.[0], from?.[1], to?.[0], to?.[1], mode]);

  useEffect(() => { ctx.setSheet('half'); }, []);

  const sel: RouteOption | null = routes ? (selectedKind === 'better' && routes.better ? routes.better : routes.fastest) : null;
  const runs = useMemo(() => (sel ? bandRuns(sel, key, kind, meta) : []), [sel, key, kind, meta]);
  const allPoints = useMemo(() => (routes ? [routes.fastest, routes.better].filter(Boolean).flatMap((r) => (r as RouteOption).path) : []), [routes]);
  const weakest = sel?.samples[sel.weakestSampleIndex];
  const worstBand: Band | null = sel ? bandOf(sel.worst, meta, kind) : null;

  const swap = () => {
    setFrom(to); setTo(from);
    setLabels((l) => ({ from: l.to, to: l.from }));
  };

  const endCard = (which: End) => {
    const value = which === 'from' ? from : to;
    const picking = pick === which;
    return (
      <div className="card flat stack tight">
        <SearchBox label={t(`route.${which}`)} placeholder={t(`walk.${which}Placeholder`)} filters={false}
          near={() => value || (() => { const c = ctx.map.getCenter(); return [c.lat, c.lng] as LatLon; })()}
          initial={labels[which] || ''} onPick={(r) => setEnd(which, [r.lat, r.lon], r.label)} />
        {value ? <AddressLine lat={value[0]} lon={value[1]} fallback={labels[which]} /> : <p className="small muted">{t('walk.notSet')}</p>}
        <div className="row wrap">
          <button className={`btn sm ${picking ? 'primary' : ''}`} type="button" onClick={() => setPick(picking ? null : which)}><Icon name="pin" size="sm" />{picking ? t('walk.tapMap') : t('walk.pick')}</button>
          {me && <button className="btn sm" type="button" onClick={() => setEnd(which, [me.lat, me.lon], null)}>{t('walk.useMine')}</button>}
          {ctx.selected && <button className="btn sm" type="button" onClick={() => setEnd(which, [ctx.selected!.lat, ctx.selected!.lon], ctx.selected!.label || null)}>{t('walk.useSelected')}</button>}
        </div>
      </div>
    );
  };

  return (
    <div className="stack">
      {/* Map layers for this view */}
      {from && <Marker position={from} icon={endIcon('start', t('route.start'))} z={1200} title={`${t('route.start')}: A`} />}
      {to && <Marker position={to} icon={endIcon('end', t('route.destination'))} z={1200} title={`${t('route.destination')}: B`} />}
      {routes && [routes.fastest, routes.better].filter((r): r is RouteOption => !!r && r !== sel).map((r) => (
        <Polyline key={r.kind} positions={r.path} options={{ color: '#454a52', weight: 5, opacity: 0.85, dashArray: '2 9', lineCap: 'round' }} tooltip={`${t(`route.${r.kind}`)} · ${t('route.min', { n: r.walkingMinutes })}`} />
      ))}
      {sel && <Polyline positions={sel.path} options={{ color: '#ffffff', weight: 11, opacity: 0.95, lineCap: 'round', lineJoin: 'round' }} />}
      {sel && runs.map((run, i) => (
        <Polyline key={`${selectedKind}${i}`} positions={run.pts} options={{ color: BAND_FILL[run.band], weight: 7, opacity: 1, lineCap: 'round', lineJoin: 'round' }} tooltip={`${t(`route.${sel.kind}`)} · ${bandText(run.band, kind)}`} />
      ))}
      {weakest && worstBand && worstBand !== 'Good' && <Marker position={[weakest.latitude, weakest.longitude]} icon={dotIcon('alert', mode === 'heat' ? 'heat' : 'safety')} title={t(`walk.weakest.${mode}`)} z={600} />}
      <FitBounds points={allPoints} fitKey={fitKey} />

      <p>{t(`walk.intro.${mode}`)}</p>
      {endCard('from')}
      {endCard('to')}
      {from && to && <button className="btn sm" type="button" onClick={swap}><Icon name="refresh" size="sm" />{t('walk.swap')}</button>}

      {loading && <><p className="small muted">{t('route.loading')}</p><Skeleton height={120} /></>}
      {error && <Banner kind="danger" icon="alert"><span>{error}</span></Banner>}
      {routes && sel && worstBand && (
        <Comparison routes={routes} sel={sel} selectedKind={selectedKind} onSelect={setSelectedKind} offlineApprox={offlineApprox}
          worstBand={worstBand} weakest={weakest} help={features && weakest ? nearestFromFeatures(features, weakest.latitude, weakest.longitude, HELP_KEYS[mode], 600) : []} />
      )}
      <p className="tiny muted">{routes && routes.source === 'straight-line' ? (offlineApprox ? t('route.offline') : t('route.straight')) : t('route.note')}</p>
    </div>
  );
}

interface ComparisonProps {
  routes: Routes;
  sel: RouteOption;
  selectedKind: 'fastest' | 'better';
  onSelect: (k: 'fastest' | 'better') => void;
  offlineApprox: boolean;
  worstBand: Band;
  weakest: CorridorSample | undefined;
  help: ReturnType<typeof nearestFromFeatures>;
}

function Comparison({ routes: r, sel, selectedKind, onSelect, offlineApprox, worstBand, weakest, help }: ComparisonProps) {
  const t = useT();
  const { mode, grid } = useResident();
  const meta = grid?.grid;
  const kind = kindOf(mode);
  const group = worstBand === 'Good' ? 'good' : worstBand === 'Fair' ? 'fair' : 'weak';

  const card = (route: RouteOption, which: 'fastest' | 'better') => {
    const avgBand = bandOf(route.average, meta, kind);
    const worst = bandOf(route.worst, meta, kind);
    const pressed = selectedKind === which;
    return (
      <button className="route-card" type="button" aria-pressed={pressed} onClick={() => onSelect(which)} key={which}>
        <div className="row between wrap">
          <h3><span className={`swatch ${which === 'fastest' && r.better ? 'dashed' : 'solid'}`} />{t(`route.${route.kind}`)}</h3>
          <b>{t('route.min', { n: route.walkingMinutes })} · {formatDistance(route.lengthMeters)}</b>
        </div>
        {which === 'better' && <div className="small">{t('route.extra', { m: formatDistance(Math.max(0, route.lengthMeters - r.fastest.lengthMeters)), n: Math.max(0, route.walkingMinutes - r.fastest.walkingMinutes) })}</div>}
        <div className="route-stats">
          <BandBadge band={avgBand}>{t(`route.avg.${mode}`, { n: Math.round(route.average) })} · {bandText(avgBand, kind)}</BandBadge>
          <BandBadge band={worst}>{t(`route.worst.${mode}`, { n: Math.round(route.worst) })}</BandBadge>
          {route.openReportsNearby > 0 && <span className="chip warn"><Icon name="flag" size="sm" />{route.openReportsNearby}</span>}
        </div>
        <div className="tiny muted">{pressed ? t('route.shown') : t('route.show')}</div>
      </button>
    );
  };

  return (
    <div className="stack">
      {r.source !== 'street' && <Banner icon={offlineApprox ? 'offline' : 'info'} small><span>{offlineApprox ? t('route.offline') : t('route.straight')}</span></Banner>}
      <div className="row between wrap">
        <b>{t('route.advice')}</b>
        <span className="row small muted">{t('legend.route')}<InfoButton onClick={() => openScoreExplainer({ layer: mode, meta })} label={t('explain.how')} /></span>
      </div>
      {card(r.fastest, 'fastest')}
      {r.better && card(r.better, 'better')}
      {r.source === 'street' && (
        <Banner kind={r.better ? 'ok' : 'info'} icon={r.better ? 'check' : 'info'} small>
          <span>{r.better ? t(`route.better.${mode}`, { g: Math.round(r.scoreGain * 10) / 10 }) : t(`route.none.${mode}`)}</span>
        </Banner>
      )}
      <div className="card">
        <p>{t(`walk.advice.${mode}.${group}`)}</p>
        {sel.openReportsNearby > 0 && <p className="small"><Icon name="flag" size="sm" /> {t('walk.reports', { n: sel.openReportsNearby })}</p>}
      </div>
      {worstBand !== 'Good' && weakest && (
        <div className="card flat stack tight">
          <b>{t(`walk.weakAt.${mode}`)}</b>
          <AddressLine lat={weakest.latitude} lon={weakest.longitude} />
          {help.length ? (
            <ul className="list small">{help.map((n) => <li className="row" key={n.key}><Icon name={FACTOR_ICON[n.key] || 'pin'} size="sm" /><span>{n.name || t(`kind.${n.kind}`)} · {formatDistance(n.distanceMeters)}</span></li>)}</ul>
          ) : <p className="small muted">{t('walk.noHelp')}</p>}
        </div>
      )}
    </div>
  );
}
