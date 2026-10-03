// Planner overview: the situation, headline numbers, how scores are spread, what is missing most, and where to act first.
// Every number can be drilled into: a tile or bar explains how it was calculated, a factor bar filters the priority list, a row opens the area drawer.

import { useEffect, useState } from 'react';
import { Banner, Empty, Explainable, Icon, InfoButton, Skeleton } from '../components/ui';
import { bandText, loadMethod, openFacts, openFactorExplainer, openKpiExplainer, openMethod } from '../components/explain';
import { ApiError, cachedGet, errorText, getReportTypes } from '../lib/api';
import { t, useT } from '../lib/i18n';
import { FACTOR_ICON, bandOf, bandRange, type Kind } from '../lib/model';
import { useApp } from '../lib/store';
import type { Band, GridMeta, PlannerSummary } from '../lib/types';
import { BandKey, HBars, Histogram, TableView } from './charts';
import { StaleBanner } from './CellDrawer';
import { openAlertDialog } from './dialogs';
import { changeEvent, loadGridFor, usePlanner } from './store';

const KPI_ORDER: Record<string, string[]> = {
  heat: ['averageScore', 'criticalCells', 'noWater500', 'noGreen500', 'openReports', 'activeAlerts', 'devicesActive'],
  night: ['averageScore', 'criticalCells', 'poorlyLit', 'noNightTransit500', 'openReports', 'activeAlerts', 'devicesActive'],
  both: ['averageScore', 'criticalCells', 'noWater500', 'poorlyLit', 'openReports', 'activeAlerts', 'devicesActive']
};
const FACTOR_OF: Record<string, string> = { noWater500: 'water', noGreen500: 'green', poorlyLit: 'lighting', noNightTransit500: 'nightTransit' };
const GO: Record<string, [string, string]> = { criticalCells: ['#/planner/map', 'ov.openMap'], openReports: ['#/planner/reports', 'ov.allReports'], activeAlerts: ['#/planner/alerts', 'pl.nav.alerts'] };
const SAFETY_TYPES = ['LightOut', 'UnsafeAtNight', 'PathHazard'];

export function Overview() {
  const t = useT();
  const event = useApp((s) => s.event);
  const res = usePlanner((s) => s.summary);
  const grid = usePlanner((s) => s.grid);
  const setGrid = usePlanner((s) => s.setGrid);
  const openCell = usePlanner((s) => s.openCell);
  const [factor, setFactor] = useState<string | null>(null);

  // The bands in charts follow the thresholds the API reports; fetch the grid once for them.
  useEffect(() => { void loadGridFor(event).then((r) => setGrid(r.data)).catch(() => {}); }, [event]);
  useEffect(() => setFactor(null), [event]);

  const error = usePlanner((s) => s.summaryError);
  const reload = usePlanner((s) => s.reloadSummary);
  if (!res && error) {
    // No data and a reason: say why (still loading on the server, offline, ...) and offer to try again.
    const loading = error instanceof ApiError && error.status === 503;
    return (
      <div className="pl-page stack">
        <Banner kind={loading ? 'info' : 'danger'} icon={loading ? 'refresh' : 'alert'}>
          <span>{loading ? t('pl.preparing') : errorText(error, t)}</span>
          <div><button className="btn sm" type="button" onClick={reload}>{t('common.retry')}</button></div>
        </Banner>
      </div>
    );
  }
  if (!res) return <div className="pl-page stack"><div className="kpis">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} height={92} />)}</div><Skeleton height={220} /></div>;

  const s = res.data;
  const meta = grid?.grid ?? { goodFrom: 75, fairFrom: 55, weakFrom: 35 } as GridMeta;
  const kind: Kind = s.event === 'heat' ? 'heat' : 'good';

  return (
    <div className="pl-page stack">
      {res.stale && <StaleBanner savedAt={res.savedAt} />}
      <Situation s={s} />
      <p className="small muted"><Icon name="info" size="sm" /> {t('ov.clickHint')}</p>
      <Kpis s={s} meta={meta} />

      <div className="grid-2">
        <section className="card">
          <div className="chart-title">
            <h2>{t('ov.distribution')}</h2>
            <span className="small muted"><Explainable onOpen={() => openKpiExplainer('cells', { value: s.cells })}>{t('ov.cellsCount', { n: s.cells })}</Explainable></span>
          </div>
          <p className="small muted">{t(`ov.distHelp.${s.event}`)}</p>
          <Histogram bins={s.histogram} meta={meta} kind={kind} onBin={(bin) => openBinExplainer(s, bin, meta, kind)} />
          <div className="row between tiny muted"><span>{t(`ov.low.${s.event}`)}</span><span>{t(`ov.high.${s.event}`)}</span></div>
          <BandKey meta={meta} kind={kind} />
          <TableView headers={[t('ov.range'), t('ov.cells')]} rows={s.histogram.map((b) => [`${b.from}–${b.to}`, b.cells])} />
        </section>

        <section className="card">
          <div className="chart-title"><h2>{t('ov.gaps')}</h2><span className="small muted">{t('ov.gapsUnit')}</span></div>
          <p className="small muted">{t('ov.gapsHelp')}</p>
          <HBars items={s.factorGaps.map((g) => ({ key: g.key, label: t(`factor.${g.key}`), value: g.weakShare, text: `${Math.round(g.weakShare)}%`, kind: g.layer.toLowerCase() }))}
            max={100} selectedKey={factor} onClick={(k) => setFactor((f) => (f === k ? null : k))} onInfo={(k) => openGapExplainer(s, k, meta)} />
          <TableView headers={[t('cell.factor'), t('ov.weakShare'), t('ov.avgScore')]} rows={s.factorGaps.map((g) => [t(`factor.${g.key}`), `${g.weakShare}%`, g.averageScore])} />
        </section>
      </div>

      <Priority s={s} meta={meta} factor={factor} setFactor={setFactor} openCell={openCell} />
      <ReportsByType s={s} event={event} />
      <p className="tiny muted">{s.notes.map((n) => <span className="block" key={n}>• {n}</span>)}</p>
    </div>
  );
}

function Situation({ s }: { s: PlannerSummary }) {
  const t = useT();
  const event = useApp((st) => st.event);
  const c = s.conditions;
  const tips: React.ReactNode[] = [];
  if (c.heat.level >= 2) {
    tips.push(
      <Banner key="heat" kind="danger" icon="thermo">
        <b>{t(`heat.${c.heat.pressure}`)}</b> {t('ov.heatActive')}
        {event !== 'heat' && <div><button className="btn sm" type="button" onClick={() => changeEvent('heat')}>{t('ov.planHeat')}</button></div>}
        <div><button className="btn sm" type="button" onClick={() => openAlertDialog({})}><Icon name="bell" size="sm" />{t('ov.sendHeatAlert')}</button></div>
      </Banner>);
  } else if (c.heat.level >= 1 && event === 'night') tips.push(<Banner key="hh" kind="info" icon="info"><span>{t('ov.heatHint')}</span></Banner>);
  if (c.isDark && event === 'heat') {
    tips.push(<Banner key="dark" kind="info" icon="moon">{t('ov.darkHint')}<div><button className="btn sm" type="button" onClick={() => changeEvent('night')}>{t('ov.planNight')}</button></div></Banner>);
  }
  if (c.hydro.elevatedGauges > 0) tips.push(<Banner key="riv" kind="danger" icon="wave"><span>{t('cond.rivers', { n: c.hydro.elevatedGauges })}</span></Banner>);
  return tips.length ? <div className="stack tight">{tips}</div> : <Banner kind="ok" icon="check"><span>{t('ov.calm')}</span></Banner>;
}

function Kpis({ s, meta }: { s: PlannerSummary; meta: GridMeta }) {
  const t = useT();
  const byKey: Record<string, { key: string; value: number; unit: string }> = Object.fromEntries(s.kpis.map((k) => [k.key, k]));
  byKey.activeAlerts = { key: 'activeAlerts', value: s.activeAlerts, unit: 'alerts' };
  byKey.devicesActive = { key: 'devicesActive', value: s.devicesActive, unit: 'devices' };
  const order = KPI_ORDER[s.event] || KPI_ORDER.both;
  const kindName = s.event === 'heat' ? t('explain.dir.heat') : s.event === 'night' ? t('explain.dir.safety') : t('explain.dir.both');

  const extra = (key: string, value: number): ([string, React.ReactNode] | null)[] => {
    switch (key) {
      case 'averageScore': { const kd: Kind = s.event === 'heat' ? 'heat' : 'good'; return [[t('ov.bandOfRange'), bandText(bandOf(value, meta, kd), kd)], [t('ov.reportLayer'), kindName], [t('kpi.cells'), s.cells]]; }
      case 'criticalCells': return [[t('kpi.cells'), s.cells], [t('kpi.criticalCells'), value], [t('ov.shareOfAll'), `${s.cells ? Math.round((100 * value) / s.cells) : 0}%`], [t('kpi.weakCells'), byKey.weakCells?.value ?? '–']];
      case 'openReports': return s.reports.filter((r) => r.open || r.last24Hours).map((r): [string, string] => [t(`rtype.${r.type}`), `${r.open} / ${r.last24Hours} / ${r.verified}`]).concat([[t('ov.reportCounts'), '']]);
      case 'activeAlerts': return [[t('kpi.activeAlerts'), s.activeAlerts]];
      case 'devicesActive': return [[t('kpi.devicesActive'), s.devicesActive]];
      default: return s.cells ? [[t('kpi.cells'), s.cells]] : [];
    }
  };

  return (
    <div className="kpis" role="list">
      {order.filter((k) => byKey[k]).map((key) => {
        const k = byKey[key];
        const alertish = (key === 'noWater500' && k.value >= 50) || (key === 'poorlyLit' && k.value >= 50);
        // Every tile opens its explanation: what it is, how it is computed, the data source and the numbers behind it.
        return (
          <button key={key} className={`kpi ${alertish ? 'alertish' : ''}`} type="button" role="listitem" title={t('explain.click')}
            onClick={() => openKpiExplainer(key, { value: k.value, unit: k.unit, factorKey: FACTOR_OF[key], extra: extra(key, k.value), links: GO[key] ? [{ href: GO[key][0], label: t(GO[key][1]) }] : [] })}>
            <div className="v num">{k.unit === '%' || k.unit === 'score' ? Math.round(k.value) : k.value}{k.unit === '%' ? <small>%</small> : k.unit === 'score' ? <small>/100</small> : null}</div>
            <div className="l">{t(`kpi.${key}`)}</div>
            <div className="h">{t(`kpi.${key}.help`)}</div>
          </button>
        );
      })}
    </div>
  );
}

function openBinExplainer(s: PlannerSummary, bin: { from: number; to: number; cells: number }, meta: GridMeta, kind: Kind) {
  const band: Band = bandOf(bin.from + 5, meta, kind);
  const [from, to] = bandRange(band, meta, kind);
  void loadMethod().catch(() => null).then((m) => {
    const layerName = s.event === 'heat' ? 'Heat' : s.event === 'night' ? 'Safety' : null;
    const meaning = m && layerName ? m.layers.find((l) => l.layer === layerName)?.bands.find((b) => b.band === band)?.meaning : t(`band.${band}.desc`);
    openFacts({
      title: t('ov.squaresInRange', { from: bin.from, to: bin.to }),
      value: bin.cells,
      valueNote: `${s.cells ? Math.round((100 * bin.cells) / s.cells) : 0}% ${t('ov.shareOfAll').toLowerCase()}`,
      band, bandKind: kind,
      intro: `${t(`ov.distHelp.${s.event}`).split('.')[0]}.`,
      sections: [
        <dl className="kv explain-kv" key="kv"><div className="kv-row"><dt>{t('ov.bandOfRange')}</dt><dd>{bandText(band, kind)} ({Math.round(from)}–{Math.round(to)})</dd></div><div className="kv-row"><dt>{t('ov.rangeMeans')}</dt><dd>{meaning || ''}</dd></div></dl>,
        <p className="small muted" key="src">{t('explain.source')}: {m ? m.kpis.find((k) => k.key === 'cells')?.source : ''}</p>
      ],
      extraFooter: <button className="btn" type="button" onClick={() => { document.querySelector('dialog[open]')?.dispatchEvent(new Event('cancel')); openMethod(meta); }}><Icon name="list" size="sm" />{t('explain.fullMethod')}</button>
    });
  });
}

/** The "gap" bar: what the percentage is, which squares it counts, and a button to the factor's weight, reason and source. */
function openGapExplainer(s: PlannerSummary, key: string, meta: GridMeta) {
  const g = s.factorGaps.find((x) => x.key === key);
  openFacts({
    title: t(`factor.${key}`),
    value: g ? `${Math.round(g.weakShare)}%` : undefined,
    valueNote: t('ov.gapsUnit'),
    intro: t('ov.gapsHelp').split('.')[0] + '.',
    sections: [
      <dl className="kv explain-kv" key="kv">
        <div className="kv-row"><dt>{t('ov.avgScore')}</dt><dd>{g ? `${g.averageScore} / 100` : '–'}</dd></div>
        <div className="kv-row"><dt>{t('explain.howComputed')}</dt><dd>{t('ov.gapFormula')}</dd></div>
        <div className="kv-row"><dt>{t('explain.source')}</dt><dd>{t('ov.gapSource')}</dd></div>
      </dl>
    ],
    extraFooter: <button className="btn" type="button" onClick={() => openFactorExplainer(key, { meta })}><Icon name="info" size="sm" />{t('explain.weight')}</button>
  });
}


function Priority({ s, meta, factor, setFactor, openCell }: { s: PlannerSummary; meta: GridMeta; factor: string | null; setFactor: (f: string | null) => void; openCell: (id: string) => void }) {
  const t = useT();
  const list = (factor ? s.topPriority.filter((c) => c.weakFactors.includes(factor)) : s.topPriority).slice(0, 12);
  return (
    <section className="card">
      <div className="chart-title">
        <h2>{t('ov.priority')} <InfoButton onClick={() => openKpiExplainer('priority', {})} label={t('ov.priorityInfo')} /></h2>
        {factor && <span className="chip accent"><Icon name={FACTOR_ICON[factor]} size="sm" />{t(`factor.${factor}`)}<button type="button" aria-label={t('ov.clearFilter')} onClick={() => setFactor(null)}><Icon name="x" size="sm" /></button></span>}
      </div>
      <p className="small muted">{t('ov.priorityHelp')}</p>
      {list.length ? (
        <div>
          {list.map((c, i) => (
            <button className="prio" type="button" key={c.cellId} onClick={() => openCell(c.cellId)}>
              <span className="rank">{i + 1}</span>
              <span>
                <span className="row between wrap">
                  <span><b>{c.label ? t('cell.near', { name: c.label }) : t('cell.title', { id: c.cellId })}</b>{c.label && <span className="tiny muted"> · {c.cellId}</span>}</span>
                  <span className="small">{t('cell.priority')} <b>{Math.round(c.priority)}</b></span>
                </span>
                <span className="meter prio-meter" data-band="Critical"><span style={{ width: `${c.priority}%`, background: 'var(--band-critical)' }} /></span>
                <span className="meta">
                  <span className="band" data-band={bandOf(c.safety, meta)}>{t('mode.safety.score')} {Math.round(c.safety)}</span>
                  <span className="band" data-band={bandOf(c.heat, meta, 'heat')}>{t('mode.heat.score')} {Math.round(c.heat)}</span>
                  {c.openReports > 0 && <span className="chip warn"><Icon name="flag" size="sm" />{c.openReports}</span>}
                  {c.weakFactors.slice(0, 3).map((f) => <span className="chip" key={f}><Icon name={FACTOR_ICON[f] || 'info'} size="sm" />{t(`factor.${f}`)}</span>)}
                </span>
              </span>
            </button>
          ))}
        </div>
      ) : <Empty icon="check">{t('ov.noPriority')}</Empty>}
      {list.length > 0 && <TableView headers={[t('ov.area'), t('cell.priority'), t('mode.safety'), t('mode.heat'), t('ov.reports')]} rows={list.map((c) => [c.cellId, Math.round(c.priority), Math.round(c.safety), Math.round(c.heat), c.openReports])} />}
      <div className="row wrap"><a className="btn" href="#/planner/map"><Icon name="map" size="sm" />{t('ov.openMap')}</a></div>
    </section>
  );
}

/** One report type: what it feeds, its weight and how long it counts, with the live counts, and the rule that turns reports into points. */
async function openReportExplainer(r: PlannerSummary['reports'][number]) {
  let types: Awaited<ReturnType<typeof getReportTypes>> = [];
  try { types = (await cachedGet('report-types', getReportTypes)).data; } catch { /* the counts alone are still useful */ }
  const info = types.find((x) => x.type === r.type);
  const m = await loadMethod().catch(() => null);
  const half = info ? (info.halfLifeHours >= 48 ? t('ov.days', { n: Math.round(info.halfLifeHours / 24) }) : t('ov.hours', { n: info.halfLifeHours })) : '–';
  openFacts({
    title: t(`rtype.${r.type}`),
    value: r.open,
    valueNote: t('ov.open'),
    sections: [
      <dl className="kv explain-kv" key="kv">
        <div className="kv-row"><dt>{t('ov.reportLayer')}</dt><dd>{info ? (info.layer === 'Heat' ? t('mode.heat.score') : t('mode.safety.score')) : '–'}</dd></div>
        <div className="kv-row"><dt>{t('ov.reportWeight')}</dt><dd>{info ? info.weight : '–'}</dd></div>
        <div className="kv-row"><dt>{t('ov.reportHalfLife')}</dt><dd>{half}</dd></div>
        <div className="kv-row"><dt>{t('ov.reportCounts')}</dt><dd>{r.open} / {r.last24Hours} / {r.verified}</dd></div>
        <div className="kv-row"><dt>{t('explain.source')}</dt><dd>{t('ov.reportSource')}</dd></div>
      </dl>,
      m ? <p className="small" key="rule">{m.reportRule}</p> : null
    ]
  });
}

function ReportsByType({ s, event }: { s: PlannerSummary; event: string }) {
  const t = useT();
  // Only the report types of the planning event: Heat shows heat reports, Night safety shows night reports.
  const rows = s.reports.filter((r) => event === 'both' || (event === 'night') === SAFETY_TYPES.includes(r.type));
  const max = Math.max(1, ...rows.map((r) => r.open));
  return (
    <section className="card">
      <div className="chart-title"><h2>{t('ov.reportsByType')}</h2><a href="#/planner/reports" className="small">{t('ov.allReports')}</a></div>
      {rows.some((r) => r.open)
        ? <HBars items={rows.map((r) => ({ key: r.type, label: t(`rtype.${r.type}`), value: r.open, text: String(r.open), kind: SAFETY_TYPES.includes(r.type) ? 'safety' : 'heat' }))} max={max}
          onClick={(k) => void openReportExplainer(rows.find((x) => x.type === k)!)} />
        : <p className="small muted">{t('ov.noReports')}</p>}
      <TableView headers={[t('rep.type'), t('ov.open'), t('ov.last24'), t('ov.verified')]} rows={rows.map((r) => [t(`rtype.${r.type}`), r.open, r.last24Hours, r.verified])} />
    </section>
  );
}
