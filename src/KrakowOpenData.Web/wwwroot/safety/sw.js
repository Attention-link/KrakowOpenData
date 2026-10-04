// Service worker: keeps the app itself (and map tiles you have looked at) available offline.
// API answers are NOT cached here; the app saves them itself in IndexedDB so it controls freshness and shows "saved" labels.

const VERSION = 'v18';
const SHELL = `krk-safety-shell-${VERSION}`;
const TILES = 'krk-safety-tiles';
const LIB = 'krk-safety-lib';
const MAX_TILES = 400;

const SHELL_FILES = [
  './', 'index.html', 'manifest.webmanifest', 'icon.svg', 'icon-32.png', 'icon-180.png', 'icon-192.png', 'icon-512.png', 'css/app.css',
  'js/main.js', 'js/geo.js', 'js/search.js', 'js/strings-resident-v2.js', 'js/strings-layers.js', 'js/strings-resident-v3.js', 'js/strings-planner-v2.js', 'js/explain.js', 'js/util.js', 'js/db.js', 'js/state.js', 'js/api.js', 'js/i18n.js', 'js/strings-resident.js', 'js/strings-planner.js',
  'js/model.js', 'js/map.js', 'js/chrome.js', 'js/resident.js', 'js/report.js', 'js/walk.js', 'js/alerts.js',
  'js/strings-a11y.js', 'js/display-boot.js', 'js/accessibility.js',
  'js/planner.js', 'js/planner-overview.js', 'js/planner-map.js', 'js/planner-reports.js', 'js/planner-alerts.js', 'js/planner-contacts.js', 'js/planner-weights.js', 'js/charts.js', 'js/planner-common.js',
  'js/access.js', 'js/wheelchair.js', 'js/strings-access.js',
  'js/telegram.js', 'js/voice.js', 'js/ai-explain.js', 'js/planner-ai.js'
];

const LIB_FILES = [
  'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.min.js',
  'https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/leaflet.min.css'
];

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const shell = await caches.open(SHELL);
    // Missing optional files must not break the install.
    await Promise.all(SHELL_FILES.map((f) => shell.add(f).catch(() => {})));
    const lib = await caches.open(LIB);
    await Promise.all(LIB_FILES.map((u) => fetch(u, { mode: 'cors' }).then((r) => r.ok && lib.put(u, r)).catch(() => {})));
    self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    for (const key of await caches.keys()) {
      if (key.startsWith('krk-safety-shell-') && key !== SHELL) await caches.delete(key);
    }
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);

  // Map tiles: cache what the user has seen, newest kept.
  if (url.hostname === 'tile.openstreetmap.org') {
    event.respondWith(tileStrategy(req));
    return;
  }
  // Leaflet from the CDN: cache-first.
  if (LIB_FILES.includes(req.url) || url.hostname === 'cdnjs.cloudflare.com') {
    event.respondWith(cacheFirst(req, LIB));
    return;
  }
  if (url.origin !== location.origin) return; // API calls and everything else go straight to the network

  // The runtime config must be fresh; fall back to the last copy offline.
  if (url.pathname.endsWith('/config.js')) {
    event.respondWith(networkFirst(req, SHELL));
    return;
  }
  // The app files: network first (so updates arrive), cache when offline.
  event.respondWith(networkFirst(req, SHELL));
});

async function networkFirst(req, cacheName) {
  const cache = await caches.open(cacheName);
  try {
    const res = await fetch(req);
    if (res.ok) cache.put(req, res.clone());
    return res;
  } catch {
    const hit = await cache.match(req, { ignoreSearch: true });
    if (hit) return hit;
    if (req.mode === 'navigate') return (await cache.match('index.html')) || (await cache.match('./')) || Response.error();
    return Response.error();
  }
}

async function cacheFirst(req, cacheName) {
  const cache = await caches.open(cacheName);
  const hit = await cache.match(req);
  if (hit) return hit;
  try {
    const res = await fetch(req);
    if (res.ok || res.type === 'opaque') cache.put(req, res.clone());
    return res;
  } catch {
    return Response.error();
  }
}

async function tileStrategy(req) {
  const cache = await caches.open(TILES);
  const hit = await cache.match(req);
  if (hit) return hit;
  try {
    const res = await fetch(req);
    if (res.ok) {
      cache.put(req, res.clone());
      trim(cache);
    }
    return res;
  } catch {
    // No tile offline: a transparent 1x1 PNG so the grid still shows on the grey background.
    return new Response(Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII='), (c) => c.charCodeAt(0)), { headers: { 'Content-Type': 'image/png' } });
  }
}

async function trim(cache) {
  const keys = await cache.keys();
  if (keys.length > MAX_TILES) await Promise.all(keys.slice(0, keys.length - MAX_TILES).map((k) => cache.delete(k)));
}
