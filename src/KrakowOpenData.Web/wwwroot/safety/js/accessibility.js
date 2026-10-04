// Accessibility statement ("Deklaracja dostępności") at #/accessibility, in English, Polish and Ukrainian: the WCAG 2.2 AA
// target, what is done, known limitations with their alternatives, keyboard shortcuts, display settings and how to report a problem.
// The contact comes from the host (Safety:AccessibilityContact, served in config.js): an e-mail address or a link.

import { h, icon } from './util.js';
import { t, getLang } from './i18n.js';
import { topbar, statusPill, displayToggles } from './chrome.js';

const PREPARED = '2026-10-04';
const KEYS = [
  ['Tab', 'stmt.key.skip'], ['Tab / Shift+Tab', 'stmt.key.tab'], ['Enter / Space', 'stmt.key.enter'],
  ['← ↑ → ↓', 'stmt.key.arrows'], ['+ / −', 'stmt.key.zoom'], ['Enter / Space', 'stmt.key.pick'], ['↓ ↑', 'stmt.key.search'], ['Esc', 'stmt.key.esc']
];

export function mountStatement(root) {
  const pill = statusPill();
  const date = new Intl.DateTimeFormat(getLang(), { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(`${PREPARED}T12:00:00`));
  const contact = String((window.KRK_CONFIG || {}).accessibilityContact || '').trim();
  const href = /^https?:\/\//.test(contact) ? contact : `mailto:${contact}`;
  const list = (prefix, n) => h('ul', { class: 'acc-list' }, Array.from({ length: n }, (_, i) => h('li', null, t(`${prefix}.${i + 1}`))));
  const section = (title, ...body) => h('section', { class: 'card stack tight' }, h('h2', null, title), ...body);

  const page = h('div', { class: 'acc-page stack' },
    h('div', null, h('a', { class: 'btn sm', href: '#/' }, icon('left', 'sm'), t('stmt.back'))),
    h('h1', null, t('stmt.title')),
    h('p', null, t('stmt.intro')),
    section(t('stmt.statusTitle'), h('p', null, t('stmt.status'))),
    section(t('stmt.doneTitle'), list('stmt.done', 8)),
    section(t('stmt.limitsTitle'), list('stmt.limits', 6)),
    section(t('stmt.keysTitle'), h('dl', { class: 'kv acc-keys' }, KEYS.flatMap(([k, key]) => [h('dt', null, h('kbd', null, k)), h('dd', null, t(key))]))),
    section(t('display.title'), displayToggles()),
    section(t('stmt.reportTitle'), h('p', null, t('stmt.reportText')),
      contact ? h('p', null, h('b', null, t('stmt.contact')), ': ', h('a', { href }, contact)) : h('p', null, t('stmt.contactNone'))),
    section(t('stmt.methodTitle'), h('p', null, t('stmt.method'))),
    h('p', { class: 'small muted' }, t('stmt.date', { date })));

  root.append(topbar({ pill }), h('main', { class: 'acc-main', id: 'main', tabindex: '-1' }, page));
  return () => pill.destroy();
}
