// Planner overview: the situation, headline numbers, how scores are spread, what is missing most, and where to act first.
// Every number can be drilled into: a factor bar filters the priority list, a row opens the area drawer.

import { h, icon, clear, formatDistance } from './util.js';
import { t } from './i18n.js';
import { P, pOn, openCellDrawer, openAlertDialog, staleBanner, loadGridFor } from './planner-common.js';
import { histogram, hbars, tableView, bandKey } from './charts.js';
import { bandOf, kindOf, bandRange, FACTOR_ICON, FACTOR_LAYER, LAYERS, modeOfEvent, REPORT_LAYER } from './model.js';
import { openKpiExplainer, openFacts, openFactorExplainer, openMethod, bandText, explainable, infoButton, loadMethod, methodNow } from './explain.js';
import { cachedGet, getReportTypes } from './api.js';


const KPI_ORDER = {
  heat: ['averageScore', 'criticalCells', 'noWater500', 'noGreen500', 'openReports', 'activeAlerts', 'devicesActive'],
  night: ['averageScore', 'criticalCells', 'poorlyLit', 'noNightTransit500', 'openReports', 'activeAlerts', 'devicesActive'],
  both: ['averageScore', 'criticalCells', 'noWater500', 'poorlyLit', 'openReports', 'activeAlerts', 'devicesActive'],
  flood: ['averageScore', 'criticalCells', 'nearRiver200', 'noEmergency1000', 'riverLevel', 'openReports', 'activeAlerts', 'devicesActive'],
  air: ['averageScore', 'criticalCells', 'nearMainRoad100', 'noTrees500', 'airLevel', 'openReports', 'activeAlerts', 'devicesActive']
};

const DEFAULT_META = { goodFrom: 75, fairFrom: 55, weakFrom: 35 };

export function mount(host) {
  const cleanups = [];
  let factor = null; // selected factor filter for the priority list
  const page = h('div', { class: 'pl-page stack' });
  host.append(page);

  function render() {
    clear(page);
    const res = P.summary;
    if (!res) {
      page.append(h('div', { class: 'kpis' }, Array.from({ length: 6 }, () => h('div', { class: 'skeleton', style: { height: '92px' } }))),
        h('div', { class: 'skeleton', style: { height: '220px' } }));
      return;
    }
    const s = res.data;
    const meta = P.grid?.grid || DEFAULT_META;
    if (res.stale) page.append(staleBanner(res.savedAt));
    page.append(situation(s));
    page.append(h('p', { class: 'small muted' }, icon('info', 'sm'), ' ', t('ov.clickHint')));
    page.append(kpis(s));

    const kind = LAYERS[modeOfEvent(s.event)].kind;
    const evKey = s.event;   // heat | night | flood | air
    page.append(h('div', { class: 'grid-2' },
      h('section', { class: 'card' },
        h('div', { class: 'chart-title' }, h('h2', null, t('ov.distribution')), h('span', { class: 'small muted' }, explainable(h('span', null, t('ov.cellsCount', { n: s.cells })), () => openKpiExplainer('cells', { value: s.cells }), null))),
        h('p', { class: 'small muted' }, t(`ov.distHelp.${s.event}`)),
        histogram(s.histogram, meta, kind, (bin, band) => openBinExplainer(s, bin, meta, kind)),
        h('div', { class: 'row between tiny muted' }, h('span', null, t(`ov.low.${evKey}`)), h('span', null, t(`ov.high.${evKey}`))),
        bandKey(meta, kind),
        tableView([t('ov.range'), t('ov.cells')], s.histogram.map((b) => [`${b.from}–${b.to}`, b.cells]))),
      h('section', { class: 'card' },
        h('div', { class: 'chart-title' }, h('h2', null, t('ov.gaps')), h('span', { class: 'small muted' }, t('ov.gapsUnit'))),
        h('p', { class: 'small muted' }, t('ov.gapsHelp')),
        hbars(s.factorGaps.map((g) => ({ key: g.key, label: t(`factor.${g.key}`), value: g.weakShare, text: `${Math.round(g.weakShare)}%`, kind: g.layer.toLowerCase() })),
          { max: 100, selectedKey: factor, onClick: (k) => { factor = factor === k ? null : k; render(); }, onInfo: (k) => openGapExplainer(s, k, meta) }),
        tableView([t('cell.factor'), t('ov.weakShare'), t('ov.avgScore')], s.factorGaps.map((g) => [t(`factor.${g.key}`), `${g.weakShare}%`, g.averageScore])))));

    page.append(priority(s, meta));
    page.append(reports(s));
    page.append(h('p', { class: 'tiny muted' }, s.notes.map((n) => h('span', { style: { display: 'block' } }, `• ${n}`))));
  }

  function situation(s) {
    const c = s.conditions;
    const wantsHeat = c.heat.level >= 1 && P.event === 'night';
    const wantsNight = c.isDark && P.event === 'heat';
    const tips = [];
    if (c.heat.level >= 2) tips.push(h('div', { class: 'banner danger' }, icon('thermo'),
      h('div', { class: 'grow' }, h('b', null, t(`heat.${c.heat.pressure}`)), t('ov.heatActive'),
        P.event !== 'heat' ? h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => switchEvent('heat') }, t('ov.planHeat'))) : null,
        h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => openAlertDialog({}) }, icon('bell', 'sm'), t('ov.sendHeatAlert'))))));
    else if (wantsHeat) tips.push(h('div', { class: 'banner info' }, icon('info'), h('span', null, t('ov.heatHint'))));
    if (wantsNight) tips.push(h('div', { class: 'banner info' }, icon('moon'), h('div', { class: 'grow' }, t('ov.darkHint'),
      h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => switchEvent('night') }, t('ov.planNight'))))));
    if (c.hydro.elevatedGauges > 0) tips.push(h('div', { class: 'banner danger' }, icon('wave'), h('div', { class: 'grow' }, t('cond.rivers', { n: c.hydro.elevatedGauges }),
      P.event !== 'flood' ? h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => switchEvent('flood') }, t('ov.planFlood'))) : null)));
    if ((c.air.pm25Average ?? c.air.pm25) >= 45) tips.push(h('div', { class: 'banner danger' }, icon('wind'), h('div', { class: 'grow' }, t('ov.airHigh', { n: Math.round(c.air.pm25Average ?? c.air.pm25) }),
      P.event !== 'air' ? h('div', null, h('button', { class: 'btn sm', type: 'button', style: { marginTop: '.4rem' }, onclick: () => switchEvent('air') }, t('ov.planAir'))) : null)));
    return tips.length ? h('div', { class: 'stack tight' }, tips) : h('div', { class: 'banner ok' }, icon('check'), h('span', null, t('ov.calm')));
  }

  function switchEvent(ev) {
    P.changeEvent?.(ev);
  }

  function kpis(s) {
    const byKey = Object.fromEntries(s.kpis.map((k) => [k.key, k]));
    byKey.activeAlerts = { key: 'activeAlerts', value: s.activeAlerts, unit: 'alerts' };
    byKey.devicesActive = { key: 'devicesActive', value: s.devicesActive, unit: 'devices' };
    const order = KPI_ORDER[s.event] || KPI_ORDER.both;
    const go = { criticalCells: ['#/planner/map', 'ov.openMap'], openReports: ['#/planner/reports', 'ov.allReports'], activeAlerts: ['#/planner/alerts', 'pl.nav.alerts'] };
    const FACTOR_OF = { noWater500: 'water', noGreen500: 'green', poorlyLit: 'lighting', noNightTransit500: 'nightTransit', nearRiver200: 'river', noEmergency1000: 'emergency', nearMainRoad100: 'traffic', noTrees500: 'trees' };
    return h('div', { class: 'kpis', role: 'list' }, order.filter((k) => byKey[k]).map((key) => {
      const k = byKey[key];
      const alertish = (key === 'noWater500' && k.value >= 50) || (key === 'poorlyLit' && k.value >= 50) || ((key === 'riverLevel' || key === 'airLevel') && k.value >= 50);
      const body = [h('div', { class: 'v num' }, k.unit === '%' ? Math.round(k.value) : k.unit === 'score' ? Math.round(k.value) : k.value, k.unit === '%' ? h('small', null, '%') : k.unit === 'score' ? h('small', null, '/100') : null),
        h('div', { class: 'l' }, t(`kpi.${key}`)), h('div', { class: 'h' }, t(`kpi.${key}.help`))];
      // Every tile opens its explanation: what it is, how it is computed, the data source and the numbers behind it.
      // The list item is a wrapper: a button cannot take role="listitem" without losing its button role.
      return h('div', { role: 'listitem', class: 'kpi-item' }, h('button', { class: `kpi ${alertish ? 'alertish' : ''}`, type: 'button', title: t('explain.click'),
        onclick: () => openKpiExplainer(key, {
          value: k.value, unit: k.unit, factorKey: FACTOR_OF[key], extra: kpiExtra(key, s, k),
          links: go[key] ? [{ href: go[key][0], label: t(go[key][1]) }] : []
        }) }, body));
    }));
  }

  /** Live numbers shown in a figure's explanation, taken from the same summary the tile came from. */
  function kpiExtra(key, s, k) {
    const byKey = Object.fromEntries(s.kpis.map((x) => [x.key, x]));
    const kindName = t(`explain.dir.${modeOfEvent(s.event)}`);
    switch (key) {
      case 'averageScore': { const kd = LAYERS[modeOfEvent(s.event)].kind; return [[t('ov.bandOfRange'), bandText(bandOf(k.value, P.grid?.grid, kd), kd)], [t('ov.reportLayer'), kindName], [t('kpi.cells'), s.cells]]; }
      case 'criticalCells': return [[t('kpi.cells'), s.cells], [t('kpi.criticalCells'), k.value], [t('ov.shareOfAll'), `${s.cells ? Math.round((100 * k.value) / s.cells) : 0}%`], [t('kpi.weakCells'), byKey.weakCells?.value ?? '–']];
      case 'openReports': return s.reports.filter((r) => r.open || r.last24Hours).map((r) => [t(`rtype.${r.type}`), `${r.open} / ${r.last24Hours} / ${r.verified}`]).concat([[t('ov.reportCounts'), '']]);
      case 'activeAlerts': return [[t('kpi.activeAlerts'), s.activeAlerts]];
      case 'devicesActive': return [[t('kpi.devicesActive'), s.devicesActive]];
      default: return s.cells ? [[t('kpi.cells'), s.cells]] : [];
    }
  }

  function openBinExplainer(s, bin, meta, kind) {
    const mid = bin.from + 5;
    const band = bandOf(mid, meta, kind);
    const [from, to] = bandRange(band, meta, kind);
    loadMethod().catch(() => null).then((m) => {
      const layerName = LAYERS[modeOfEvent(s.event)].api;
      const meaning = m && layerName ? m.layers.find((l) => l.layer === layerName)?.bands.find((b) => b.band === band)?.meaning : t(`band.${band}.desc`);
      openFacts({
        title: t('ov.squaresInRange', { from: bin.from, to: bin.to }),
        value: bin.cells, valueNote: `${s.cells ? Math.round((100 * bin.cells) / s.cells) : 0}% ${t('ov.shareOfAll').toLowerCase()}`,
        band, bandKind: kind,
        intro: `${t(`ov.distHelp.${s.event}`).split('.')[0]}.`,
        sections: [
          h('dl', { class: 'kv explain-kv' }, h('dt', null, t('ov.bandOfRange')), h('dd', null, `${bandText(band, kind)} (${Math.round(from)}–${Math.round(to)})`), h('dt', null, t('ov.rangeMeans')), h('dd', null, meaning || '')),
          h('p', { class: 'small muted' }, t('explain.source') + ': ' + (m ? m.kpis.find((k) => k.key === 'cells')?.source : ''))
        ],
        extraFooter: h('button', { class: 'btn', type: 'button', onclick: () => { document.querySelector('dialog[open]')?.close(); openMethod(meta); } }, icon('list', 'sm'), t('explain.fullMethod'))
      });
    });
  }

  /** The "gap" bar: what the percentage is, which squares it counts, and a button to the factor's weight, reason and source. */
  function openGapExplainer(s, key, meta) {
    const g = s.factorGaps.find((x) => x.key === key);
    openFacts({
      title: t(`factor.${key}`),
      value: g ? `${Math.round(g.weakShare)}%` : undefined,
      valueNote: t('ov.gapsUnit'),
      intro: t('ov.gapsHelp').split('.')[0] + '.',
      sections: [
        h('dl', { class: 'kv explain-kv' },
          h('dt', null, t('ov.avgScore')), h('dd', null, g ? `${g.averageScore} / 100` : '–'),
          h('dt', null, t('explain.howComputed')), h('dd', null, t('ov.gapFormula')),
          h('dt', null, t('explain.source')), h('dd', null, t('ov.gapSource'))),
      ],
      extraFooter: h('button', { class: 'btn', type: 'button', onclick: () => { document.querySelector('dialog[open]')?.close(); openFactorExplainer(key, { meta }); } }, icon('info', 'sm'), t('explain.weight'))
    });
  }

  function priority(s, meta) {
    const all = s.topPriority;
    const list = (factor ? all.filter((c) => c.weakFactors.includes(factor)) : all).slice(0, 12);
    return h('section', { class: 'card' },
      h('div', { class: 'chart-title' }, h('h2', null, t('ov.priority'), ' ', infoButton(() => openKpiExplainer('priority', {}), t('ov.priorityInfo'))),
        factor ? h('span', { class: 'chip accent' }, icon(FACTOR_ICON[factor], 'sm'), t(`factor.${factor}`), h('button', { type: 'button', 'aria-label': t('ov.clearFilter'), onclick: () => { factor = null; render(); } }, icon('x', 'sm'))) : null),
      h('p', { class: 'small muted' }, t('ov.priorityHelp')),
      list.length ? h('div', null, list.map((c, i) => h('button', { class: 'prio', type: 'button', onclick: () => openCellDrawer(c.cellId, { meta }) },
        h('span', { class: 'rank' }, i + 1),
        h('span', null,
          h('span', { class: 'row between wrap' }, h('span', null, h('b', null, c.label ? t('cell.near', { name: c.label }) : t('cell.title', { id: c.cellId })), c.label ? h('span', { class: 'tiny muted' }, ` · ${c.cellId}`) : null),
            h('span', { class: 'small' }, `${t('cell.priority')} `, h('b', null, Math.round(c.priority)))),
          h('span', { class: 'meter', 'data-band': 'Critical', style: { display: 'block', margin: '.3rem 0' } }, h('span', { style: { width: `${c.priority}%`, background: 'var(--band-critical)' } })),
          h('span', { class: 'meta' },
            h('span', { class: 'band', 'data-band': bandOf(c[modeOfEvent(s.event)], meta, LAYERS[modeOfEvent(s.event)].kind) }, `${t(`mode.${modeOfEvent(s.event)}.score`)} ${Math.round(c[modeOfEvent(s.event)])}`),
            c.openReports ? h('span', { class: 'chip warn' }, icon('flag', 'sm'), c.openReports) : null,
            c.weakFactors.slice(0, 3).map((f) => h('span', { class: 'chip' }, icon(FACTOR_ICON[f] || 'info', 'sm'), t(`factor.${f}`)))))))) : h('div', { class: 'empty' }, icon('check'), h('p', null, t('ov.noPriority'))),
      list.length ? tableView([t('ov.area'), t('cell.priority'), t(`mode.${modeOfEvent(s.event)}`), t('ov.reports')], list.map((c) => [c.cellId, Math.round(c.priority), Math.round(c[modeOfEvent(s.event)]), c.openReports])) : null,
      h('div', { class: 'row wrap', style: { marginTop: '.6rem' } }, h('a', { class: 'btn', href: '#/planner/map' }, icon('map', 'sm'), t('ov.openMap'))));
  }

  /** One report type: what it feeds, its weight and how long it counts, with the live counts, and the rule that turns reports into points. */
  async function openReportExplainer(r) {
    let types = [];
    try { types = (await cachedGet('report-types', getReportTypes)).data; } catch { /* the counts alone are still useful */ }
    const info = types.find((x) => x.type === r.type);
    const m = await loadMethod().catch(() => null);
    const half = info ? (info.halfLifeHours >= 48 ? t('ov.days', { n: Math.round(info.halfLifeHours / 24) }) : t('ov.hours', { n: info.halfLifeHours })) : '–';
    openFacts({
      title: t(`rtype.${r.type}`),
      value: r.open,
      valueNote: `${t('ov.open')}`,
      sections: [
        h('dl', { class: 'kv explain-kv' },
          h('dt', null, t('ov.reportLayer')), h('dd', null, info ? t(`mode.${REPORT_LAYER[info.type] || 'safety'}.score`) : '–'),
          h('dt', null, t('ov.reportWeight')), h('dd', null, info ? info.weight : '–'),
          h('dt', null, t('ov.reportHalfLife')), h('dd', null, half),
          h('dt', null, t('ov.reportCounts')), h('dd', null, `${r.open} / ${r.last24Hours} / ${r.verified}`),
          h('dt', null, t('explain.source')), h('dd', null, t('ov.reportSource'))),
        m ? h('p', { class: 'small' }, m.reportRule) : null
      ]
    });
  }

  function reports(s) {
    // Only the report types of the planning event (the API already sends just those).
    const rows = s.reports.filter((r) => (REPORT_LAYER[r.type] || 'safety') === modeOfEvent(P.event));
    const max = Math.max(1, ...rows.map((r) => r.open));
    return h('section', { class: 'card' },
      h('div', { class: 'chart-title' }, h('h2', null, t('ov.reportsByType')), h('a', { href: '#/planner/reports', class: 'small' }, t('ov.allReports'))),
      rows.some((r) => r.open) ? hbars(rows.map((r) => ({
        key: r.type, label: t(`rtype.${r.type}`), value: r.open, text: String(r.open), kind: REPORT_LAYER[r.type] || 'safety'
      })), { max, onClick: (k) => openReportExplainer(rows.find((x) => x.type === k)) }) : h('p', { class: 'small muted' }, t('ov.noReports')),
      tableView([t('rep.type'), t('ov.open'), t('ov.last24'), t('ov.verified')], rows.map((r) => [t(`rtype.${r.type}`), r.open, r.last24Hours, r.verified])));
  }

  cleanups.push(pOn('summary', render));
  cleanups.push(pOn('event', () => { factor = null; render(); }));
  cleanups.push(pOn('summaryError', (e) => { if (!P.summary) { clear(page); page.append(h('div', { class: 'banner danger' }, icon('alert'), h('span', null, t('err.generic')))); } }));
  // The bands in charts follow the thresholds the API reports; fetch the grid once for them.
  loadGridFor().then((r) => { P.grid = r.data; render(); }).catch(() => {});
  render();
  return () => cleanups.forEach((fn) => fn());
}
