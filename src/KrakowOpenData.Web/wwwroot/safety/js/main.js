// Entry point: language, theme, routing between the resident view and the planner dashboard, service worker.

import { state, on } from './state.js';
import { applyDocumentLanguage } from './i18n.js';
import { applyTheme, startConnectivity, flushOutbox } from './chrome.js';
import { mountResident } from './resident.js';

const app = document.getElementById('app');
let unmount = null;
let mounting = Promise.resolve();

async function route() {
  const hash = location.hash || '#/';
  const wantsPlanner = hash.startsWith('#/planner');
  mounting = mounting.then(async () => {
    unmount?.();
    unmount = null;
    app.replaceChildren();
    applyDocumentLanguage();
    if (wantsPlanner) {
      const { mountPlanner } = await import('./planner.js');
      unmount = mountPlanner(app, hash.split('/')[2] || 'overview');
    } else {
      unmount = mountResident(app);
    }
  });
  return mounting;
}

window.addEventListener('hashchange', route);
on('lang', route);
applyTheme();
startConnectivity(() => flushOutbox());
route();

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('sw.js').catch((e) => console.warn('Service worker not registered', e));
  });
}
