// Accessibility statement ("Deklaracja dostępności") at #/accessibility, in English, Polish and Ukrainian: the WCAG 2.2 AA
// target, what is done, known limitations with their alternatives, keyboard shortcuts, display settings and how to report a problem.
// The contact comes from the host (Safety:AccessibilityContact, served in config.js): an e-mail address or a link.

import { h, icon } from './util.js';
import { t, getLang } from './i18n.js';
import { topbar, statusPill, displayToggles } from './chrome.js';

const PREPARED = '2026-10-04';
const KEYS = [
  ['Tab', 'acc.key.skip'], ['Tab / Shift+Tab', 'acc.key.tab'], ['Enter / Space', 'acc.key.enter'],
  ['← ↑ → ↓', 'acc.key.arrows'], ['+ / −', 'acc.key.zoom'], ['Enter / Space', 'acc.key.pick'], ['↓ ↑', 'acc.key.search'], ['Esc', 'acc.key.esc']
];

export function mountStatement(root) {
  const pill = statusPill();
  const date = new Intl.DateTimeFormat(getLang(), { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(`${PREPARED}T12:00:00`));
  const contact = String((window.KRK_CONFIG || {}).accessibilityContact || '').trim();
  const href = /^https?:\/\//.test(contact) ? contact : `mailto:${contact}`;
  const list = (prefix, n) => h('ul', { class: 'acc-list' }, Array.from({ length: n }, (_, i) => h('li', null, t(`${prefix}.${i + 1}`))));
  const section = (title, ...body) => h('section', { class: 'card stack tight' }, h('h2', null, title), ...body);

  const page = h('div', { class: 'acc-page stack' },
    h('div', null, h('a', { class: 'btn sm', href: '#/' }, icon('left', 'sm'), t('acc.back'))),
    h('h1', null, t('acc.title')),
    h('p', null, t('acc.intro')),
    section(t('acc.statusTitle'), h('p', null, t('acc.status'))),
    section(t('acc.doneTitle'), list('acc.done', 8)),
    section(t('acc.limitsTitle'), list('acc.limits', 6)),
    section(t('acc.keysTitle'), h('dl', { class: 'kv acc-keys' }, KEYS.flatMap(([k, key]) => [h('dt', null, h('kbd', null, k)), h('dd', null, t(key))]))),
    section(t('display.title'), displayToggles()),
    section(t('acc.reportTitle'), h('p', null, t('acc.reportText')),
      contact ? h('p', null, h('b', null, t('acc.contact')), ': ', h('a', { href }, contact)) : h('p', null, t('acc.contactNone'))),
    section(t('acc.methodTitle'), h('p', null, t('acc.method'))),
    h('p', { class: 'small muted' }, t('acc.date', { date })));

  root.append(topbar({ pill }), h('main', { class: 'acc-main', id: 'main', tabindex: '-1' }, page));
  return () => pill.destroy();
}
