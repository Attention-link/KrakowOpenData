// The place card: scores with their meaning, where to find relief or help, why the score is what it is, resident reports.

import { useState } from 'react';
import { Banner, BandBadge, Icon, InfoButton, Meter, Skeleton, toast } from '../components/ui';
import {
  bandText, effectText, openFactorExplainer, openMethod, openScoreExplainer, scoreSummary
} from '../components/explain';
import { AddressLine } from '../lib/geo';
import { confirmReport, errorText } from '../lib/api';
import { useLang, useT } from '../lib/i18n';
import { COL, FACTOR_ICON, FACTOR_LAYER, RELIEF, bandOf, kindOf, nearestFromFeatures } from '../lib/model';
import { setApp, useApp, useOffline } from '../lib/store';
import { clamp, directionsLink, formatDistance, timeAgo } from '../lib/util';
import { ls } from '../lib/storage';
import type { Band, Factor, LayerKey, LayerScore, Mode, Place, Report } from '../lib/types';
import { layersOf, useResident } from './context';

export function PlaceView() {
  const t = useT();
  const ctx = useResident();
  const sel = ctx.selected;
  const offline = useOffline();
  const { mode: m, grid, gridIndex } = ctx;
  if (!sel) return null;

  const layers = layersOf(m);
  const place = sel.place;
  const meta = grid?.grid;
  const radius = meta?.searchRadiusMeters || 1500;
  const gridRow = sel.row !== undefined ? gridIndex.get(`${sel.row}-${sel.col}`) : null;
  const approx = !place && !!gridRow && !sel.loading;

  const header = (
    <div className="stack tight">
      <AddressLine lat={sel.lat} lon={sel.lon} fallback={sel.label || (place?.label ? t('cell.near', { name: place.label }) : null)} />
      <div className="small muted"><Icon name="target" size="sm" /> {m === 'heat' ? t('cov.heat', { r: formatDistance(radius) }) : m === 'safety' ? t('cov.safety', { r: formatDistance(radius) }) : t('cov.both', { r: formatDistance(radius) })}</div>
      {sel.stale && place && sel.savedAt && <span className="chip warn"><Icon name="offline" size="sm" />{t('place.saved', { when: timeAgo(sel.savedAt, t, useApp.getState().lang) })}</span>}
    </div>
  );

  if (sel.loading && !gridRow) {
    return <div className="stack">{header}<Skeleton height={84} /><Skeleton height={160} /></div>;
  }

  const layerData = (k: LayerKey): LayerScore | null => place ? place[k]
    : gridRow ? { score: gridRow[COL[k]], band: bandOf(gridRow[COL[k]], meta, kindOf(k)), factors: [], reportPenalty: 0, baseScore: gridRow[COL[k]] } : null;
  const data = Object.fromEntries(layers.map((k) => [k, layerData(k)])) as Record<LayerKey, LayerScore | null>;
  const combined = place ? place.combined : gridRow ? gridRow[COL.combined] : null;

  if (layers.some((k) => !data[k])) {
    // No score for this spot: offline and not in the saved map, or outside the mapped area.
    const offlineNoScore = offline && !!ctx.features;
    return (
      <div className="stack">
        {header}
        {sel.error ? (
          <Banner kind={offlineNoScore ? 'info' : 'danger'} icon={offlineNoScore ? 'offline' : 'alert'}>
            {offlineNoScore ? t('place.noSaved') : errorText(sel.error, t)}
            <div><button className="btn sm" type="button" onClick={() => ctx.selectPoint(sel.lat, sel.lon, { keepView: true, label: sel.label })}>{t('common.retry')}</button></div>
          </Banner>
        ) : <Banner kind="info" icon="info"><span>{t('place.outside')}</span></Banner>}
        <PlaceActions />
        {Boolean(sel.error) && offlineNoScore && <Nearby place={null} />}
      </div>
    );
  }

  const wanted = new Set<string>(layers.map((k) => (k === 'heat' ? 'Heat' : 'Safety')));
  const reports = (place?.reports || []).filter((r) => wanted.has(r.layer));
  const penalty = place && layers.some((k) => place[k].reportPenalty > 0);

  return (
    <div className="stack">
      {header}

      {m === 'both' && combined !== null && (() => {
        const b = bandOf(combined, meta);
        return (
          <div className="row overall" title={scoreSummary('both', combined, b)}>
            <Ring score={combined} band={b} label={t('mode.both.score')} />
            <div className="grow">
              <div className="row"><BandBadge band={b}>{t(`band.${b}`)}</BandBadge><span className="small muted">{t('explain.dir.both')}</span>
                <InfoButton onClick={() => openScoreExplainer({ layer: 'both', place, meta })} label={t('explain.how')} /></div>
              <p className="small muted">{t('place.combinedHelp')}</p>
            </div>
          </div>
        );
      })()}

      <div className="place-scores">
        {layers.map((k) => <ScoreBox key={k} kind={k} layer={data[k]!} big={m !== 'both'} place={place} />)}
      </div>

      {approx && <Banner kind="info" icon="offline" small><span>{t('place.approx')}</span></Banner>}
      {sel.loading && <Skeleton height={120} />}
      {penalty && <Banner icon="flag" small><span>{t('place.reportsAffect')}</span></Banner>}

      <PlaceActions />
      <Nearby place={place} />
      {m !== 'safety' && <WaterNote place={place} />}

      {place && (
        <details className="card flat" open>
          <summary>{t('place.why')}</summary>
          <div className="stack">
            {layers.map((k) => (
              <div key={k}>
                <p className="sect-title">
                  {k === 'heat'
                    ? `${t('mode.heat.score')} · ${Math.round(place[k].baseScore)}${place[k].reportPenalty ? ` + ${place[k].reportPenalty} ${t('place.fromReports')}` : ''} = ${Math.round(place[k].score)}`
                    : `${t('mode.safety.score')} · ${Math.round(place[k].baseScore)}${place[k].reportPenalty ? ` − ${place[k].reportPenalty} ${t('place.fromReports')}` : ''} = ${Math.round(place[k].score)}`}
                </p>
                {place[k].factors.map((f) => <FactorRow key={f.key} f={f} layerKey={k} />)}
              </div>
            ))}
            <div className="row wrap">
              <button className="btn sm" type="button" onClick={() => openScoreExplainer({ layer: m, place, meta })}><Icon name="info" size="sm" />{t('explain.how')}</button>
              <button className="btn sm quiet" type="button" onClick={() => openMethod(meta)}><Icon name="list" size="sm" />{t('explain.fullMethod')}</button>
            </div>
            <p className="tiny muted">{t('place.whyNote')}</p>
          </div>
        </details>
      )}

      {reports.length > 0 && (
        <section>
          <h3>{t('place.reportsTitle')}</h3>
          <ul className="list">{reports.slice(0, 6).map((r) => <ReportRow key={r.id} r={r} />)}</ul>
        </section>
      )}
      <p className="tiny muted">{t('how.notCrime')}</p>
    </div>
  );
}

function Ring({ score, band, label }: { score: number; band: Band; label: string }) {
  const t = useT();
  const r = 34;
  const circ = 2 * Math.PI * r;
  const off = circ * (1 - clamp(score, 0, 100) / 100);
  const colors: Record<Band, string> = { Good: '#2a78d6', Fair: '#9ec5f4', Weak: '#ee9b95', Critical: '#d03b3b' };
  return (
    <div className="ring" role="img" aria-label={`${label}: ${Math.round(score)} / 100, ${t(`band.${band}`)}`}>
      <svg viewBox="0 0 84 84" aria-hidden="true">
        <circle className="track" cx="42" cy="42" r={r} fill="none" strokeWidth="9" />
        <circle className="val" cx="42" cy="42" r={r} fill="none" strokeWidth="9" strokeLinecap="round" strokeDasharray={circ} strokeDashoffset={off} style={{ stroke: colors[band] }} />
      </svg>
      <div className="center"><b>{Math.round(score)}</b><small>/100</small></div>
    </div>
  );
}

/** One score: the number, its band in words, which way is good, and a "?" that explains it with this place's numbers. */
function ScoreBox({ kind, layer, big, place }: { kind: LayerKey; layer: LayerScore; big: boolean; place: Place | null }) {
  const t = useT();
  const { grid } = useResident();
  return (
    <div className={`score-box ${big ? 'active' : ''}`} title={scoreSummary(kind, layer.score, layer.band)}>
      <div className="grow">
        <div className="lbl"><Icon name={kind === 'safety' ? 'moon' : 'sun'} size="sm" />{t(`mode.${kind}.score`)}
          <InfoButton onClick={() => openScoreExplainer({ layer: kind, place, meta: grid?.grid })} label={`${t('explain.how')} ${t(`mode.${kind}.score`)}`} /></div>
        <div className="row"><span className="big num">{Math.round(layer.score)}</span><BandBadge band={layer.band}>{bandText(layer.band, kindOf(kind))}</BandBadge></div>
        <div className="tiny muted">{t(kind === 'heat' ? 'explain.dir.heat' : 'explain.dir.safety')}</div>
        <Meter band={layer.band} value={layer.score} />
      </div>
    </div>
  );
}

/** A factor of the score. Clicking it explains the factor: weight, why, data source and how many are mapped. */
function FactorRow({ f, layerKey }: { f: Factor; layerKey: LayerKey }) {
  const t = useT();
  const { grid } = useResident();
  const meta = grid?.grid;
  const band = bandOf(f.score, meta);
  const radius = meta?.searchRadiusMeters || 1500;
  const value = f.unit === 'm' ? (f.value === null ? t('factor.none', { r: formatDistance(radius) }) : formatDistance(f.value)) : `${Math.round(f.value ?? 0)} ${t('factor.lamps')}`;
  const effect = effectText(layerKey, f);
  return (
    <button className="factor" type="button" title={`${t(`factor.${f.key}`)}: ${value} · ${effect} · ${t('explain.click')}`} onClick={() => openFactorExplainer(f.key, { factor: f, layer: layerKey, meta })}>
      <span className={FACTOR_LAYER[f.key] === 'heat' ? 'chip heat' : 'chip safety'} style={{ padding: '.15rem' }}><Icon name={FACTOR_ICON[f.key] || 'info'} size="sm" /></span>
      <div><div className="strong">{t(`factor.${f.key}`)}</div>{f.nearestName ? <div className="tiny muted truncate">{f.nearestName}</div> : <div className="tiny muted">{effect}</div>}</div>
      <div className="val">{value}</div>
      <Meter band={band} value={f.score} />
    </button>
  );
}

/** "Where to cool down / safe places nearby": from the API answer, or from the saved feature list offline. */
function Nearby({ place }: { place: Place | null }) {
  const t = useT();
  const { mode, selected: sel, features, grid } = useResident();
  if (!sel) return null;
  const keys = RELIEF[mode];
  const radius = grid?.grid.searchRadiusMeters || 1500;
  const nearest = place ? place.nearest.filter((n) => keys.includes(n.key)) : features ? nearestFromFeatures(features, sel.lat, sel.lon, keys) : [];
  return (
    <section>
      <h3>{t(mode === 'heat' ? 'place.nearHeat' : mode === 'safety' ? 'place.nearSafety' : 'place.nearBoth')}</h3>
      {nearest.length ? <div>{nearest.map((n) => (
        <div className="near" key={n.key}>
          <div className={`ico ${FACTOR_LAYER[n.key] || 'heat'}`}><Icon name={FACTOR_ICON[n.key] || 'pin'} /></div>
          <div className="grow">
            <div className="strong">{n.name || t(`kind.${n.kind}`)}</div>
            <div className="small muted">{`${t(`factor.${n.key}`)} · ${formatDistance(n.distanceMeters)} · ${t('place.walkMin', { n: n.walkingMinutes })}${n.openingHours ? ` · ${n.openingHours}` : ''}`}</div>
          </div>
          <a className="btn icon quiet" href={directionsLink(n.latitude, n.longitude)} target="_blank" rel="noopener noreferrer" aria-label={`${t('place.directions')}: ${n.name || t(`kind.${n.kind}`)}`}><Icon name="external" size="sm" /></a>
        </div>
      ))}</div> : <p className="small muted">{t('place.nothingNear', { r: formatDistance(radius) })}</p>}
    </section>
  );
}

/** Honest note about thin water data when the nearest drinking-water point is far or missing. */
function WaterNote({ place }: { place: Place | null }) {
  const t = useT();
  const { features } = useResident();
  const count = features ? features.filter((f) => f.key === 'water').length : null;
  const water = place?.heat.factors.find((f) => f.key === 'water');
  if (!place || !water || water.score >= 35 || !count) return null;
  return <Banner icon="info" small><span>{t('place.waterGap', { n: count })}</span></Banner>;
}

function ReportRow({ r }: { r: Report }) {
  const t = useT();
  const lang = useLang();
  const ctx = useResident();
  const [confirmed, setConfirmed] = useState<string[]>(() => ls.get<string[]>('confirmed') || []);
  const mine = confirmed.includes(r.id);
  const [busy, setBusy] = useState(false);
  const offline = useOffline();
  const click = async () => {
    setBusy(true);
    try {
      await confirmReport(r.id);
      const next = [...confirmed, r.id].slice(-100);
      ls.set('confirmed', next);
      setConfirmed(next);
      toast(t('report.thanksConfirm'));
      ctx.refreshGrid();
      if (ctx.selected) ctx.selectPoint(ctx.selected.lat, ctx.selected.lon, { keepView: true, label: ctx.selected.label });
    } catch (err) {
      toast(errorText(err, t), { error: true });
    } finally { setBusy(false); }
  };
  return (
    <li className="row between wrap">
      <div>
        <div className="strong">{t(`rtype.${r.type}`)}</div>
        <div className="small muted">{`${t('report.supporters', { n: r.supporters })} · ${timeAgo(r.lastActivityAt, t, lang)}${r.verifiedByPlanner ? ' · ' + t('report.verified') : ''}`}</div>
      </div>
      <button className="btn sm" type="button" disabled={mine || offline || busy} onClick={click}>{mine ? t('report.confirmed') : t('report.stillTrue')}</button>
    </li>
  );
}

function PlaceActions() {
  const t = useT();
  const ctx = useResident();
  const sel = ctx.selected!;
  const mode: Mode = ctx.mode;
  return (
    <div className="row wrap">
      <button className="btn primary" type="button" onClick={() => ctx.showView('report')}><Icon name="flag" size="sm" />{t('place.report')}</button>
      <button className="btn" type="button" onClick={() => ctx.showView('walk', { to: [sel.lat, sel.lon], toLabel: sel.label })}><Icon name="walk" size="sm" />{t(`place.walkTo.${mode}`)}</button>
      <button className="btn" type="button" onClick={() => { setApp({ me: { lat: sel.lat, lon: sel.lon, source: 'manual' } }); ctx.refreshAlerts(); toast(t('place.areaSet')); }}><Icon name="bell" size="sm" />{t('place.setArea')}</button>
      <a className="btn" href={directionsLink(sel.lat, sel.lon)} target="_blank" rel="noopener noreferrer"><Icon name="external" size="sm" />{t('place.directions')}</a>
    </div>
  );
}

