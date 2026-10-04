// Small, dependency-free charts for the planner dashboard. Each chart has a table alternative for screen readers
// and for people who prefer numbers. Colours: score bands (blue to red) for scores; one hue for plain counts.

import { h, svg } from './util.js';
import { t } from './i18n.js';
import { BAND_FILL, bandOf, bandRange } from './model.js';

/** Score histogram: 10 bins of 10 points, each bar coloured by the band its range falls in. */
export function histogram(bins, meta, kind = 'good', onClick = null) {
  const max = Math.max(1, ...bins.map((b) => b.cells));
  // A group, not an image: the bars inside are buttons that must stay reachable.
  const wrap = h('div', { class: 'hist', role: 'group', 'aria-label': t('chart.histAria') });
  for (const b of bins) {
    const band = bandOf(b.from + 5, meta, kind);
    const bar = h(onClick ? 'button' : 'div', { class: 'bar', type: onClick ? 'button' : null, title: `${b.from}–${b.to}: ${b.cells}${onClick ? ' · ' + t('explain.click') : ''}`,
      onclick: onClick ? () => onClick(b, band) : null, 'aria-label': onClick ? `${b.from}–${b.to}: ${b.cells}` : null },
      h('span', { class: 'n' }, b.cells ? String(b.cells) : ''),
      h('span', { style: { height: `${Math.max(2, (b.cells / max) * 100)}%`, background: BAND_FILL[band] } }),
      h('small', null, String(b.from)));
    wrap.append(bar);
  }
  return wrap;
}

/** Colour key for the four score bands, with the score range of each (kind 'heat' = ranges of the heat score). */
export function bandKey(meta, kind = 'good') {
  return h('div', { class: 'row wrap tiny', style: { marginTop: '.4rem' } },
    ['Critical', 'Weak', 'Fair', 'Good'].map((b) => {
      const [from, to] = bandRange(b, meta, kind);
      return h('span', { class: 'band', 'data-band': b }, `${t(kind === 'heat' ? `band.heat.${b}` : `band.${b}`)} ${Math.round(from)}–${Math.round(to)}`);
    }));
}

/**
 * Horizontal bars. items: [{key, label, value, text, kind}]; onClick(key) optional; selectedKey highlights one.
 * onInfo(key) adds a small "how is this calculated" button to every row (a row cannot nest a button, so rows with onClick
 * and onInfo sit in a wrapper next to the info button).
 */
export function hbars(items, { max = 100, onClick, selectedKey, onInfo } = {}) {
  const wrap = h('div', { role: onClick ? 'group' : 'list' });
  for (const it of items) {
    const interactive = onClick || onInfo;
    const row = h(onClick ? 'button' : 'div', {
      class: `hbar ${it.kind || ''}`, type: onClick ? 'button' : null,
      'aria-pressed': onClick ? String(selectedKey === it.key) : null,
      onclick: onClick ? () => onClick(it.key) : null,
      role: onClick || onInfo ? null : 'listitem'
    },
      h('span', { class: 'truncate' }, it.label),
      h('span', { class: 'track' }, h('span', { class: 'fill', style: { width: `${Math.min(100, (it.value / max) * 100)}%` } })),
      h('span', { class: 'n num' }, it.text ?? `${Math.round(it.value)}`));
    if (onInfo) {
      wrap.append(h('div', { class: 'hbar-row', role: interactive ? 'group' : null }, row,
        h('button', { class: 'info-btn', type: 'button', title: t('explain.click'), 'aria-label': `${t('explain.how')}: ${it.label}`, onclick: () => onInfo(it.key) }, '!')));
    } else wrap.append(row);
  }
  return wrap;
}

/** Accessible table version of a chart, inside a <details>. */
export function tableView(headers, rows) {
  return h('details', { style: { marginTop: '.6rem' } },
    h('summary', { class: 'small', style: { cursor: 'pointer' } }, t('chart.asTable')),
    h('div', { class: 'tbl-wrap' },
      h('table', { class: 't' },
        h('thead', null, h('tr', null, headers.map((x) => h('th', { scope: 'col' }, x)))),
        h('tbody', null, rows.map((r) => h('tr', null, r.map((c) => h('td', null, String(c)))))))));
}

/** A circular score gauge as SVG (0-100), used in the cell drawer. */
export function gauge(score, meta, label, kind = 'good') {
  const r = 30, circ = 2 * Math.PI * r;
  const band = bandOf(score, meta, kind);
  const el = svg('svg', { viewBox: '0 0 76 76', width: 76, height: 76, role: 'img', 'aria-label': `${label}: ${Math.round(score)}` },
    svg('circle', { cx: 38, cy: 38, r, fill: 'none', stroke: 'var(--surface-3)', 'stroke-width': 8 }),
    svg('circle', { cx: 38, cy: 38, r, fill: 'none', stroke: BAND_FILL[band], 'stroke-width': 8, 'stroke-linecap': 'round',
      'stroke-dasharray': circ, 'stroke-dashoffset': circ * (1 - Math.max(0, Math.min(100, score)) / 100), transform: 'rotate(-90 38 38)' }),
    svg('text', { x: 38, y: 44, 'text-anchor': 'middle', 'font-size': 22, 'font-weight': 800, fill: 'currentColor' }, String(Math.round(score))));
  return el;
}
