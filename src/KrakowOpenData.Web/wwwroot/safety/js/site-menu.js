// The site's one menu, behind the "Menu" button in every page's top bar. The list comes from the web app
// (Navigation/SiteMenu.cs, served as site-menu.json), which also renders the portal's side menu from it, so the portal and this
// app always offer the same way around. Offline, the service worker answers with the last saved copy; without one, only
// the home page is offered.

import { h, icon, openDialog } from './util.js';
import { state } from './state.js';
import { t } from './i18n.js';

let pending = null;

function load() {
  pending ||= fetch('site-menu.json')
    .then((r) => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json(); })
    .catch((e) => { pending = null; throw e; });
  return pending;
}

/** True for the entry of the page being shown: same page and same app route (#/access is not #/accessibility). */
export function isCurrent(href, loc = location) {
  const u = new URL(href, loc.href);
  if (u.origin !== loc.origin || u.pathname !== loc.pathname) return false;
  const here = loc.hash || '#/';
  const there = u.hash || '#/';
  return there === '#/' ? here === '#/' : here === there || here.startsWith(`${there}/`);
}

function entry(item, close) {
  const current = isCurrent(item.href);
  return h('li', null, h('a', {
    class: `menu-link${item.staff ? ' staff' : ''}`, href: item.href, 'aria-current': current ? 'page' : null,
    // An app route changes only the hash: close the menu so the new view is not hidden behind it.
    onclick: () => close('nav')
  },
  h('span', { class: 'menu-icon', 'aria-hidden': 'true' }, item.icon || ''),
  h('span', { class: 'grow' }, item.label),
  item.staff ? h('span', { class: 'menu-staff' }, icon('lock', 'sm'), ' ', item.staffLabel) : ''));
}

export async function openSiteMenu() {
  let menu = null;
  try {
    const all = await load();
    menu = all[state.lang] || all.en || null;
  } catch {
    menu = null;
  }

  openDialog((close) => {
    const body = menu
      ? h('nav', { class: 'site-menu', 'aria-label': menu.title },
        ...menu.groups.map((g) => h('div', { class: 'menu-group' },
          g.heading ? h('h3', null, g.heading) : '',
          h('ul', null, g.items.map((i) => entry({ ...i, staffLabel: menu.staff }, close))))),
        h('div', { class: 'menu-group menu-foot' }, h('ul', null, menu.footer.map((i) => entry(i, close)))))
      : h('nav', { class: 'site-menu', 'aria-label': t('nav.menu') },
        h('p', { class: 'small muted' }, t('nav.menuOffline')),
        h('ul', null, entry({ href: '/', label: t('nav.home'), icon: '' }, close)));
    return { title: menu?.title || t('nav.menu'), body };
  }, { className: 'sheet' });
}
