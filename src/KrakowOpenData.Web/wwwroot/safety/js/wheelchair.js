// The wheelchair status of a mapped place or stop, as words and a symbol (never colour alone).
// value: 'yes' | 'limited' | 'no' | null/undefined. null = no data, shown grey as "brak danych", never as accessible.

import { h } from './util.js';
import { t } from './i18n.js';

const MARK = { yes: '✓', limited: '!', no: '✕', unknown: '?' };

export const wheelchairKey = (value) => (value === 'yes' || value === 'limited' || value === 'no' ? value : 'unknown');

/** "♿ tak" / "♿ ograniczona" / "♿ nie" / "♿ brak danych" as plain text. */
export const wheelchairText = (value) => `♿ ${t(`acc.status.${wheelchairKey(value)}`)}`;

/** A small badge: status class for colour, a mark for shape-independent meaning, and the word. */
export function wheelchairBadge(value) {
  const key = wheelchairKey(value);
  return h('span', { class: `acc-badge ${key}`, title: `${t('acc.wheelchair')}: ${t(`acc.status.${key}`)}` },
    h('span', { 'aria-hidden': 'true' }, `♿ ${MARK[key]}`), ' ',
    h('span', { class: 'sr-only' }, `${t('acc.wheelchair')}: `),
    t(`acc.status.${key}`));
}
