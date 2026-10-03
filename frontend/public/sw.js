// Service worker: keeps the app itself (and map tiles you have looked at) available offline.
// API answers are NOT cached here; the app saves them itself in IndexedDB so it controls freshness and shows "saved" labels.
// The built files have hashed names, so nothing is listed in advance: pages are fetched network-first and every file the
// app loads is kept for the next visit.

const VERSION = 'v3';
const SHELL = `krk-shell-${VERSION}`;
const TILES = 'krk-tiles';
const MAX_TILES = 400;

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(SHELL).then((c) => c.addAll(['./', 'index.html', 'manifest.webmanifest', 'icon.svg']).catch(() => {})).then(() => self.skipWaiting()));
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    for (const key of await caches.keys()) {
      if (key.startsWith('krk-shell-') && key !== SHELL) await caches.delete(key);
    }
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);

  // Map tiles: cache what the user has seen, newest kept.
  if (url.hostname === 'tile.openstreetmap.org') { event.respondWith(tiles(req)); return; }
  if (url.origin !== location.origin) return;                 // API calls and everything else go straight to the network

  // The runtime config must be fresh; fall back to the last copy offline.
  if (url.pathname.endsWith('/config.js')) { event.respondWith(networkFirst(req)); return; }
  // Pages: network first, so a new version is never hidden; the saved page when offline.
  if (req.mode === 'navigate') { event.respondWith(networkFirst(req, 'index.html')); return; }
  // Built files and images: the saved copy at once, refreshed in the background.
  event.respondWith(staleWhileRevalidate(req));
});

async function networkFirst(req, fallbackKey) {
  const cache = await caches.open(SHELL);
  try {
    const res = await fetch(req);
    if (res.ok) cache.put(req, res.clone());
    return res;
  } catch {
    return (await cache.match(req)) || (fallbackKey && (await cache.match(fallbackKey))) || (await cache.match('./')) || Response.error();
  }
}

async function staleWhileRevalidate(req) {
  const cache = await caches.open(SHELL);
  const hit = await cache.match(req);
  const refresh = fetch(req).then((res) => { if (res.ok) cache.put(req, res.clone()); return res; }).catch(() => null);
  return hit || (await refresh) || Response.error();
}

async function tiles(req) {
  const cache = await caches.open(TILES);
  const hit = await cache.match(req);
  if (hit) return hit;
  try {
    const res = await fetch(req);
    if (res.ok) {
      cache.put(req, res.clone());
      const keys = await cache.keys();
      if (keys.length > MAX_TILES) await Promise.all(keys.slice(0, keys.length - MAX_TILES).map((k) => cache.delete(k)));
    }
    return res;
  } catch {
    return Response.error();
  }
}
