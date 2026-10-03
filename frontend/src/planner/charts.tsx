// Small, dependency-free charts for the planner dashboard. Each chart has a table alternative for screen readers
// and for people who prefer numbers. Colours: score bands (blue to red) for scores; one hue for plain counts.

import type { ReactNode } from 'react';
import { BAND_FILL, bandOf, bandRange, type Kind } from '../lib/model';
import { useT } from '../lib/i18n';
import type { Band, GridMeta } from '../lib/types';

type Meta = Pick<GridMeta, 'goodFrom' | 'fairFrom' | 'weakFrom'> | null | undefined;

/** Score histogram: 10 bins of 10 points, each bar coloured by the band its range falls in. */
export function Histogram({ bins, meta, kind = 'good', onBin }: { bins: { from: number; to: number; cells: number }[]; meta: Meta; kind?: Kind; onBin?: (bin: { from: number; to: number; cells: number }, band: Band) => void }) {
  const t = useT();
  const max = Math.max(1, ...bins.map((b) => b.cells));
  return (
    <div className="hist" role="img" aria-label={t('chart.histAria')}>
      {bins.map((b) => {
        const band = bandOf(b.from + 5, meta, kind);
        const inner = (
          <>
            <span className="n">{b.cells ? String(b.cells) : ''}</span>
            <span className="fill" style={{ height: `${Math.max(2, (b.cells / max) * 100)}%`, background: BAND_FILL[band] }} />
            <small>{b.from}</small>
          </>
        );
        return onBin
          ? <button key={b.from} type="button" className="bar" title={`${b.from}–${b.to}: ${b.cells} · ${t('explain.click')}`} aria-label={`${b.from}–${b.to}: ${b.cells}`} onClick={() => onBin(b, band)}>{inner}</button>
          : <div key={b.from} className="bar" title={`${b.from}–${b.to}: ${b.cells}`}>{inner}</div>;
      })}
    </div>
  );
}

/** Colour key for the four score bands, with the score range of each. */
export function BandKey({ meta, kind = 'good' }: { meta: Meta; kind?: Kind }) {
  const t = useT();
  return (
    <div className="row wrap tiny band-key">
      {(['Critical', 'Weak', 'Fair', 'Good'] as Band[]).map((b) => {
        const [from, to] = bandRange(b, meta, kind);
        return <span key={b} className="band" data-band={b}>{t(kind === 'heat' ? `band.heat.${b}` : `band.${b}`)} {Math.round(from)}–{Math.round(to)}</span>;
      })}
    </div>
  );
}

export interface BarItem { key: string; label: string; value: number; text?: string; kind?: string }

/** Horizontal bars. `onClick` makes each bar a button; `onInfo` adds a "?" next to every bar. */
export function HBars({ items, max = 100, onClick, selectedKey, onInfo }: { items: BarItem[]; max?: number; onClick?: (key: string) => void; selectedKey?: string | null; onInfo?: (key: string) => void }) {
  const t = useT();
  return (
    <div role={onClick ? 'group' : 'list'}>
      {items.map((it) => {
        const Row = onClick ? 'button' : 'div';
        const row = (
          <Row key={it.key} className={`hbar ${it.kind || ''}`} {...(onClick ? { type: 'button' as const, 'aria-pressed': selectedKey === it.key, onClick: () => onClick(it.key) } : { role: onInfo ? undefined : 'listitem' })}>
            <span className="truncate">{it.label}</span>
            <span className="track"><span className="fill" style={{ width: `${Math.min(100, (it.value / max) * 100)}%` }} /></span>
            <span className="n num">{it.text ?? Math.round(it.value)}</span>
          </Row>
        );
        return onInfo
          ? <div className="hbar-row" key={it.key}>{row}<button className="info-btn" type="button" title={t('explain.click')} aria-label={`${t('explain.how')}: ${it.label}`} onClick={() => onInfo(it.key)}>?</button></div>
          : row;
      })}
    </div>
  );
}

/** Accessible table version of a chart, inside a <details>. */
export function TableView({ headers, rows }: { headers: string[]; rows: ReactNode[][] }) {
  const t = useT();
  return (
    <details className="table-view">
      <summary className="small">{t('chart.asTable')}</summary>
      <div className="tbl-wrap">
        <table className="t">
          <thead><tr>{headers.map((x) => <th scope="col" key={x}>{x}</th>)}</tr></thead>
          <tbody>{rows.map((r, i) => <tr key={i}>{r.map((c, j) => <td key={j}>{c}</td>)}</tr>)}</tbody>
        </table>
      </div>
    </details>
  );
}

/** A circular score gauge as SVG (0-100), used in the area drawer. */
export function Gauge({ score, meta, label, kind = 'good' }: { score: number; meta: Meta; label: string; kind?: Kind }) {
  const r = 30;
  const circ = 2 * Math.PI * r;
  const band = bandOf(score, meta, kind);
  return (
    <svg viewBox="0 0 76 76" width={76} height={76} role="img" aria-label={`${label}: ${Math.round(score)}`}>
      <circle cx={38} cy={38} r={r} fill="none" stroke="var(--surface-3)" strokeWidth={8} />
      <circle cx={38} cy={38} r={r} fill="none" stroke={BAND_FILL[band]} strokeWidth={8} strokeLinecap="round" strokeDasharray={circ}
        strokeDashoffset={circ * (1 - Math.max(0, Math.min(100, score)) / 100)} transform="rotate(-90 38 38)" />
      <text x={38} y={44} textAnchor="middle" fontSize={22} fontWeight={800} fill="currentColor">{Math.round(score)}</text>
    </svg>
  );
}
