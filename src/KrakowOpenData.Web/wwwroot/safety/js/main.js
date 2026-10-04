// Entry point: language, theme, routing between the resident view and the planner dashboard, service worker.
// Routes: #/ (map and menu), #/access (Dostępność view), #/notifications (menu, at the Telegram card), #/accessibility
// (accessibility statement), #/planner[/page]. The portal, the Telegram bot and printed QR codes link to the first three.

import { state, on } from './state.js';
import { applyDocumentLanguage, t } from './i18n.js';
import { applyTheme, applyDisplay, startConnectivity, flushOutbox, stopReading } from './chrome.js';
import { mountResident } from './resident.js';
import { setUiText } from './util.js';

const app = document.getElementById('app');
let unmount = null;
let mounting = Promise.resolve();
setUiText(t);

async function route() {
  const hash = location.hash || '#/';
  const wantsPlanner = hash.startsWith('#/planner');
  mounting = mounting.then(async () => {
    unmount?.();
    unmount = null;
    stopReading();
    app.replaceChildren();
    applyDocumentLanguage();
    if (wantsPlanner) {
      const { mountPlanner } = await import('./planner.js');
      unmount = mountPlanner(app, hash.split('/')[2] || 'overview');
    } else if (hash.startsWith('#/accessibility')) {
      const { mountStatement } = await import('./accessibility.js');
      unmount = mountStatement(app);
    } else {
      const start = /^#\/access(\/|$)/.test(hash) ? 'access' : /^#\/notifications(\/|$)/.test(hash) ? 'notifications' : null;
      unmount = mountResident(app, { start });
    }
  });
  return mounting;
}

// Focus is never dropped on a rebuild: after a page change it goes to the main content, after a language change back to the
// language menu (the whole page is rebuilt in the new language).
const focusAfter = (sel) => () => {
  const el = document.querySelector(sel);
  if (el && !el.contains(document.activeElement)) el.focus({ preventScroll: true });
};
window.addEventListener('hashchange', () => route().then(focusAfter('#main')));
on('lang', () => route().then(focusAfter('#lang-select')));
applyTheme();
applyDisplay();
startConnectivity(() => flushOutbox());
route();

// The skip link must not change the hash (that is the router): it moves focus to the main content instead.
document.querySelector('.skip-link')?.addEventListener('click', (e) => {
  const target = document.getElementById('main');
  if (!target) return;
  e.preventDefault();
  target.focus();
});

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('sw.js').catch((e) => console.warn('Service worker not registered', e));
  });
}

// Tables turn into stacked cards on phones (see css/app.css): every cell shows its column title through data-label.
function labelTableCells() {
  app.querySelectorAll('table.t').forEach((table) => {
    const heads = [...table.querySelectorAll('thead th')].map((th) => th.textContent.trim());
    table.querySelectorAll('tbody tr').forEach((row) => {
      [...row.children].forEach((cell, i) => { if (heads[i] && cell.getAttribute('data-label') !== heads[i]) cell.setAttribute('data-label', heads[i]); });
    });
  });
}
new MutationObserver(labelTableCells).observe(app, { childList: true, subtree: true });
