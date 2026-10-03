// "What does this mean and why?" for every score and statistic. The numbers, weights, thresholds, reasons and data sources
// come from the API (/api/safety/method, built from the same code that computes the scores), so the explanation cannot drift
// from the model. Used by the resident cards and legend, and by the planner dashboard (every figure is clickable).

import type { ReactNode } from 'react';
import { Icon, InfoButton, openDialog, closeAllDialogs } from './ui';
import { t } from '../lib/i18n';
import { cachedGet, getMethod, peek } from '../lib/api';
import { BAND_FILL, FACTOR_ICON, bandRange, kindOf, type Kind } from '../lib/model';
import { formatDistance } from '../lib/util';
import type { Band, Factor, GridMeta, LayerKey, LayerMethod, Method, Mode, Place } from '../lib/types';

// ── Method data (loaded once, saved for offline) ─────────────────────────────
let method: Method | null = null;
let pending: Promise<Method> | null = null;

export const methodNow = () => method;

/** Forgets the loaded method (used by tests, and to force a fresh load). */
export function resetMethodCache() { method = null; pending = null; }

export function loadMethod(): Promise<Method> {
  if (method) return Promise.resolve(method);
  pending ||= cachedGet('method', getMethod).then((r) => { method = r.data; return method; }).finally(() => { pending = null; });
  return pending;
}

void peek<Method>('method').then((s) => { if (s && !method) method = s.data; }).catch(() => {});

const layerOf = (key: 'Heat' | 'Safety'): LayerMethod | null => method?.layers.find((l) => l.layer === key) || null;
const factorInfo = (key: string) => method?.layers.flatMap((l) => l.factors).find((f) => f.key === key) || null;

/** The text of a band: heat has its own words (Low heat ... Very high heat), the other scores Good ... Critical. */
export const bandText = (band: Band, kind: Kind = 'good') => t(kind === 'heat' ? `band.heat.${band}` : `band.${band}`);

/** One-line description of a score for a tooltip (the `title` attribute) and screen readers. */
export function scoreSummary(layer: string, score: number, band: Band): string {
  const dir = layer === 'heat' ? t('explain.dir.heat') : layer === 'safety' ? t('explain.dir.safety') : t('explain.dir.both');
  return `${Math.round(score)} / 100 · ${bandText(band, kindOf(layer))} · ${dir}`;
}

// ── Small building blocks ────────────────────────────────────────────────────
const Kv = ({ rows }: { rows: ([ReactNode, ReactNode] | null | false)[] }) => (
  <dl className="kv explain-kv">
    {rows.filter((r): r is [ReactNode, ReactNode] => !!r).map(([k, v], i) => <div className="kv-row" key={i}><dt>{k}</dt><dd>{v}</dd></div>)}
  </dl>
);

const Section = ({ title, children }: { title?: ReactNode; children: ReactNode }) => (
  <section className="explain-sec">{title && <h3>{title}</h3>}{children}</section>
);

const WeightBar = ({ weight }: { weight: number }) => (
  <span className="wbar" role="img" aria-label={`${weight} / 100`}><span style={{ width: `${weight}%` }} /></span>
);

function BandTable({ layer, meta }: { layer: LayerMethod; meta?: GridMeta | null }) {
  const kind = kindOf(layer.layer);
  return (
    <ul className="list small band-list">
      {layer.bands.map((b) => {
        const [from, to] = bandRange(b.band, meta, kind);
        return (
          <li className="row" key={b.band}>
            <span className="band range" data-band={b.band}>{Math.round(from)}–{Math.round(to)}</span>
            <span><b>{bandText(b.band, kind)}</b> · {b.meaning}</span>
          </li>
        );
      })}
    </ul>
  );
}

function valueText(f: Pick<Factor, 'unit' | 'value'>, radius?: number): string {
  if (f.unit === 'm') return f.value === null || f.value === undefined ? t('factor.none', { r: formatDistance(radius || 1500) }) : formatDistance(f.value);
  return `${Math.round(f.value ?? 0)} ${t('factor.lamps')}`;
}

/** What a measured factor did to the score, in words and numbers ("adds 25 points of heat"). */
export function effectText(layerKey: string, f: Pick<Factor, 'contribution' | 'weight'>): string {
  const pts = Math.round(f.contribution * 10) / 10;
  if (layerKey === 'heat') return pts > 0 ? t('explain.addsHeat', { n: pts }) : t('explain.addsNoHeat');
  return t('explain.addsSafety', { n: pts, max: f.weight });
}

// ── The dialog ───────────────────────────────────────────────────────────────
interface FactsProps {
  title: ReactNode;
  value?: ReactNode;
  valueNote?: ReactNode;
  band?: Band;
  bandKind?: Kind;
  intro?: ReactNode;
  sections?: ReactNode[];
  extraFooter?: ReactNode;
}

/** Opens a dialog with a headline value, an intro and sections. */
export function openFacts({ title, value, valueNote, band, bandKind, intro, sections = [], extraFooter }: FactsProps) {
  return openDialog((close) => ({
    title,
    body: (
      <div className="stack explain">
        {value !== undefined && value !== null && (
          <div className="explain-head">
            <span className="explain-value num">{value}</span>
            {band && <span className="band" data-band={band}>{bandText(band, bandKind)}</span>}
            {valueNote && <span className="small muted">{valueNote}</span>}
          </div>
        )}
        {intro && <p>{intro}</p>}
        {sections.map((s, i) => <div key={i}>{s}</div>)}
      </div>
    ),
    footer: <>{extraFooter}<button className="btn primary" type="button" onClick={() => close('ok')}>{t('common.done')}</button></>
  }));
}

function needMethod(open: () => void) {
  if (method) { open(); return; }
  loadMethod().then(open).catch(() => openFacts({ title: t('explain.title'), intro: t('explain.unavailable') }));
}

const FullMethodButton = ({ meta }: { meta?: GridMeta | null }) => (
  <button className="btn" type="button" onClick={() => { closeAllDialogs(); openMethod(meta); }}><Icon name="list" size="sm" />{t('explain.fullMethod')}</button>
);

// ── Score explainer (what it means, the scale, why this place got it) ───────
interface ScoreExplainerArgs { layer: Mode; place?: Place | null; meta?: GridMeta | null }

/** layer: 'heat' | 'safety' | 'both'. With a place, the dialog also lists, factor by factor, what produced that number. */
export function openScoreExplainer({ layer, place = null, meta = null }: ScoreExplainerArgs) {
  needMethod(() => {
    const m = method!;
    const layers: LayerKey[] = layer === 'both' ? ['safety', 'heat'] : [layer];
    const first = layers[0];
    const firstData = place ? place[first] : null;

    openFacts({
      title: layer === 'both' ? t('explain.titleBoth') : layerOf(first === 'heat' ? 'Heat' : 'Safety')!.title,
      value: layer === 'both' ? (place ? Math.round(place.combined) : undefined) : (firstData ? Math.round(firstData.score) : undefined),
      valueNote: layer === 'both' || !firstData ? null : '/ 100',
      band: layer === 'both' ? place?.combinedBand : firstData?.band,
      bandKind: layer === 'both' ? 'good' : kindOf(first),
      sections: [
        layer === 'both' && (
          <Section title={t('explain.overallTitle')}>
            <p>{m.combinedFormula}</p>
            {place && <Kv rows={[[t('explain.overall'), `${Math.round(place.combined)} / 100 · ${bandText(place.combinedBand)}`]]} />}
          </Section>
        ),
        ...layers.map((key) => {
          const L = layerOf(key === 'heat' ? 'Heat' : 'Safety')!;
          const data = place ? place[key] : null;
          return (
            <Section key={key} title={L.title}>
              <p className="small"><b>{L.direction}</b></p>
              <p className="small">{L.meaning}</p>
              {data && <p className="explain-now">{t('explain.thisPlace')}: <b>{Math.round(data.score)} / 100</b> · {bandText(data.band, kindOf(key))}</p>}
              <h4>{t('explain.scale')}</h4>
              <BandTable layer={L} meta={meta} />
              {data && <WhyPlace layerKey={key} data={data} meta={meta} />}
              <h4>{t('explain.formula')}</h4>
              <p className="small">{L.formula}</p>
              {key === 'heat' && m.temperatureC !== null && (
                <p className="small"><Icon name="thermo" size="sm" /> {t('explain.liveHeat', { c: m.temperatureC.toFixed(1), p: t(`heat.${m.heatPressure}`) })}</p>
              )}
            </Section>
          );
        }),
        <Section key="reports" title={t('explain.reportsTitle')}><p className="small">{m.reportRule}</p></Section>,
        <ul key="limits" className="list tiny muted">{m.limits.map((l) => <li key={l}>{l}</li>)}</ul>
      ].filter(Boolean) as ReactNode[],
      extraFooter: <FullMethodButton meta={meta} />
    });
  });
}

/** The factor table of one place: measured value, weight, what it added, and how many such features are mapped. */
function WhyPlace({ layerKey, data, meta }: { layerKey: LayerKey; data: Place['heat']; meta?: GridMeta | null }) {
  const reports = data.reportPenalty;
  const total = Math.round(data.score);
  const top = layerKey === 'heat' ? [...data.factors].sort((a, b) => b.contribution - a.contribution).filter((f) => f.contribution > 0).slice(0, 2) : [];
  const round = (n: number) => Math.round(n * 10) / 10;
  return (
    <div>
      <h4>{t('explain.whyThis')}</h4>
      {layerKey === 'heat' && (
        <p className="small">{top.length ? t('explain.heatBiggest', { list: top.map((f) => `${t(`factor.${f.key}`)} (+${Math.round(f.contribution)})`).join(', ') }) : t('explain.heatNone')}</p>
      )}
      <div className="tbl-wrap">
        <table className="t">
          <thead><tr>{[t('cell.factor'), t('cell.value'), t('cell.weight'), layerKey === 'heat' ? t('explain.colHeat') : t('explain.colSafety'), t('explain.colMapped')].map((x) => <th scope="col" key={x}>{x}</th>)}</tr></thead>
          <tbody>
            {data.factors.map((f) => {
              const info = factorInfo(f.key);
              return (
                <tr key={f.key}>
                  <td><button className="linklike" type="button" onClick={() => openFactorExplainer(f.key, { factor: f, layer: layerKey, meta })}><span className="row"><Icon name={FACTOR_ICON[f.key] || 'info'} size="sm" />{t(`factor.${f.key}`)}</span></button></td>
                  <td>{valueText(f, meta?.searchRadiusMeters)}{f.nearestName && <div className="tiny muted">{f.nearestName}</div>}</td>
                  <td className="num">{f.weight}</td>
                  <td className="num">+{round(f.contribution)}</td>
                  <td className="num muted">{info ? info.mappedCount.toLocaleString() : '–'}</td>
                </tr>
              );
            })}
            {reports ? <tr><td colSpan={3}>{layerKey === 'heat' ? t('explain.reportsAddHeat') : t('explain.reportsSubtract')}</td><td className="num">{layerKey === 'heat' ? `+${reports}` : `−${reports}`}</td><td /></tr> : null}
            <tr><td colSpan={3}><b>{t('explain.total')}</b></td><td className="num"><b>{total}</b></td><td /></tr>
          </tbody>
        </table>
      </div>
      <p className="tiny muted">{layerKey === 'heat' ? t('explain.heatTableNote') : t('explain.safetyTableNote')}</p>
    </div>
  );
}

// ── One factor ───────────────────────────────────────────────────────────────
export function openFactorExplainer(key: string, { factor = null, layer = null, meta = null }: { factor?: Factor | null; layer?: string | null; meta?: GridMeta | null } = {}) {
  needMethod(() => {
    const info = factorInfo(key);
    if (!info) { openFacts({ title: t(`factor.${key}`), intro: t('explain.unavailable') }); return; }
    const layerKey = layer || (info.layer === 'Heat' ? 'heat' : 'safety');
    openFacts({
      title: t(`factor.${key}`),
      value: `${info.weight}%`,
      valueNote: t('explain.ofScore', { layer: layerKey === 'heat' ? t('mode.heat') : t('mode.safety') }),
      sections: [
        <Kv key="kv" rows={[
          [t('explain.measures'), info.measures],
          [t('explain.weight'), <span className="row" key="w"><b>{info.weight} / 100</b><WeightBar weight={info.weight} /></span>],
          [t('explain.fullScore'), info.fullScoreAt],
          [t('explain.zeroScore'), info.zeroScoreAt],
          [t('explain.why'), info.whyWeighted],
          [t('explain.source'), info.source],
          [t('explain.mapped'), `${info.mappedCount.toLocaleString()}${key === 'lighting' ? ' ' + t('explain.lamps') : ''}`]
        ]} />,
        info.dataCaveat && <div key="cv" className="banner small"><Icon name="info" size="sm" /><span>{info.dataCaveat}</span></div>,
        factor && (
          <Section key="here" title={t('explain.hereTitle')}>
            <Kv rows={[
              [t('explain.hereValue'), valueText(factor, meta?.searchRadiusMeters)],
              factor.nearestName ? [t('explain.hereNearest'), factor.nearestName] : null,
              [t('explain.hereScore'), `${Math.round(factor.score)} / 100`],
              [t('explain.hereEffect'), effectText(layerKey, factor)]
            ]} />
          </Section>
        )
      ].filter(Boolean) as ReactNode[]
    });
  });
}

// ── The whole method, with weights and sources ───────────────────────────────
export function openMethod(meta: GridMeta | null = null) {
  needMethod(() => {
    const m = method!;
    openFacts({
      title: t('explain.methodTitle'),
      sections: [
        <p key="intro">{t('explain.methodIntro')}</p>,
        ...m.layers.map((L) => (
          <Section key={L.layer} title={L.title}>
            <p className="small"><b>{L.direction}</b></p>
            <p className="small">{L.formula}</p>
            <h4>{t('explain.weightsTitle')}</h4>
            {L.factors.map((f) => (
              <div className="factor-doc" key={f.key}>
                <div className="row between">
                  <b className="row"><Icon name={FACTOR_ICON[f.key] || 'info'} size="sm" />{t(`factor.${f.key}`)}</b>
                  <span className="row"><b>{f.weight}</b><WeightBar weight={f.weight} /></span>
                </div>
                <p className="small">{f.whyWeighted}</p>
                <p className="tiny muted">{t('explain.fullScore')}: {f.fullScoreAt} · {t('explain.zeroScore')}: {f.zeroScoreAt}</p>
                <p className="tiny muted">{t('explain.source')}: {f.source} · {t('explain.mapped')}: {f.mappedCount.toLocaleString()}</p>
                {f.dataCaveat && <p className="tiny"><Icon name="info" size="sm" /> {f.dataCaveat}</p>}
              </div>
            ))}
            <h4>{t('explain.scale')}</h4>
            <BandTable layer={L} meta={meta} />
          </Section>
        )),
        <Section key="overall" title={t('explain.overallTitle')}><p className="small">{m.combinedFormula}</p></Section>,
        <Section key="reports" title={t('explain.reportsTitle')}><p className="small">{m.reportRule}</p></Section>,
        <Section key="priority" title={t('explain.priorityTitle')}><p className="small">{m.priorityFormula}</p><p className="small">{m.exposureFormula}</p></Section>,
        <ul key="limits" className="list tiny muted">{m.limits.map((l) => <li key={l}>{l}</li>)}</ul>
      ]
    });
  });
}

// ── Planner statistics ───────────────────────────────────────────────────────
interface KpiArgs {
  value?: number | string | null;
  unit?: string;
  extra?: ([ReactNode, ReactNode] | null)[];
  links?: { href: string; label: string }[];
  title?: string;
  factorKey?: string;
}

/**
 * Explains one dashboard figure: what it is, how it is computed, where its data comes from, and the numbers behind it.
 * key: a KPI key from the method (cells, criticalCells, noWater500, ...). extra: [[label, text]] with live numbers from the dashboard.
 */
export function openKpiExplainer(key: string, { value, unit, extra = [], links = [], title, factorKey }: KpiArgs = {}) {
  needMethod(() => {
    const m = method!;
    const info = m.kpis.find((k) => k.key === key);
    const fi = factorKey ? factorInfo(factorKey) : null;
    const rows = [...extra] as ([ReactNode, ReactNode] | null)[];
    if (fi) rows.push(
      [t('explain.weight'), `${fi.weight} / 100 (${fi.layer === 'Heat' ? t('mode.heat') : t('mode.safety')})`],
      [t('explain.mapped'), fi.mappedCount.toLocaleString()],
      [t('explain.why'), fi.whyWeighted]);
    const shown = value === undefined || value === null ? undefined : unit === '%' ? `${Math.round(Number(value))}%` : unit === 'score' ? `${Math.round(Number(value))} / 100` : String(value);
    openFacts({
      title: title || info?.title || t(`kpi.${key}`),
      value: shown,
      intro: info?.definition || t('explain.unavailable'),
      sections: [
        info && <Section key="how" title={t('explain.howComputed')}><p className="small">{info.formula}</p><p className="tiny muted">{t('explain.source')}: {info.source}</p></Section>,
        key === 'priority' && <Section key="prio" title={t('explain.priorityTitle')}><p className="small">{m.exposureFormula}</p></Section>,
        rows.length > 0 && <Section key="nums" title={t('explain.numbersBehind')}><Kv rows={rows} /></Section>,
        links.length > 0 && <div key="links" className="row wrap">{links.map((l) => <a key={l.href} className="btn sm" href={l.href} onClick={() => closeAllDialogs()}>{l.label}</a>)}</div>
      ].filter(Boolean) as ReactNode[]
    });
  });
}

// ── Map legend content (shared by the resident and planner maps) ─────────────
interface LegendProps { mode: Mode; meta?: GridMeta | null; compact?: boolean; onExplain?: (layer: Mode) => void }

/**
 * Legend body for a view: for each score shown, its direction ("higher = hotter"), the coloured ranges with names and a
 * button to the full explanation.
 */
export function LegendBody({ mode, meta, compact = false, onExplain }: LegendProps) {
  void method; // re-render after the method loads is triggered by callers; band meanings fall back to static texts
  const block = (kind: Kind, title: string, sub: string, explainLayer: Mode) => (
    <div className="legend-block" key={title}>
      <div className="row between"><b className="small">{title}</b><InfoButton onClick={() => onExplain?.(explainLayer)} label={t('explain.how')} /></div>
      <div className="tiny muted">{sub}</div>
      <ul className="legend-rows">
        {(['Good', 'Fair', 'Weak', 'Critical'] as Band[]).map((b) => {
          const [from, to] = bandRange(b, meta, kind);
          const layerName = kind === 'heat' ? 'Heat' : mode === 'both' ? null : 'Safety';
          const meaning = (layerName && layerOf(layerName)?.bands.find((x) => x.band === b)?.meaning) || t(kind === 'heat' ? `band.heat.${b}.desc` : `band.${b}.desc`);
          return (
            <li key={b}>
              <i style={{ background: BAND_FILL[b] }} />
              <span className="num">{Math.round(from)}–{Math.round(to)}</span>
              <span><b>{bandText(b, kind)}</b>{!compact && <> · {meaning}</>}</span>
            </li>
          );
        })}
      </ul>
    </div>
  );
  if (mode === 'heat') return block('heat', t('legend.heatTitle'), t('explain.dir.heat'), 'heat');
  if (mode === 'safety') return block('good', t('legend.safetyTitle'), t('explain.dir.safety'), 'safety');
  return <>{block('good', t('legend.bothTitle'), t('explain.dir.both'), 'both')}<div className="tiny muted">{t('legend.bothNote')}</div></>;
}
