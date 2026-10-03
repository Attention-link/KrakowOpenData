// Tiny key-value store on IndexedDB (falls back to memory when IndexedDB is unavailable, e.g. private windows).
// Used to keep the last good API answers so the app still works with no connection.

const DB_NAME = 'krk-safety';
const STORE = 'kv';
let dbPromise;
const memory = new Map();

function open() {
  if (dbPromise) return dbPromise;
  dbPromise = new Promise((resolve) => {
    try {
      const req = indexedDB.open(DB_NAME, 1);
      req.onupgradeneeded = () => req.result.createObjectStore(STORE);
      req.onsuccess = () => resolve(req.result);
      req.onerror = () => resolve(null);
      req.onblocked = () => resolve(null);
    } catch {
      resolve(null);
    }
  });
  return dbPromise;
}

function run(db, mode, fn) {
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const req = fn(tx.objectStore(STORE));
    tx.oncomplete = () => resolve(req?.result);
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

export async function kvGet(key) {
  const db = await open();
  if (!db) return memory.get(key);
  try { return await run(db, 'readonly', (s) => s.get(key)); } catch { return memory.get(key); }
}

export async function kvSet(key, value) {
  const db = await open();
  if (!db) { memory.set(key, value); return; }
  try { await run(db, 'readwrite', (s) => s.put(value, key)); } catch { memory.set(key, value); }
}

export async function kvDel(key) {
  memory.delete(key);
  const db = await open();
  if (!db) return;
  try { await run(db, 'readwrite', (s) => s.delete(key)); } catch { /* ignore */ }
}

export async function kvClearAll() {
  memory.clear();
  const db = await open();
  if (!db) return;
  try { await run(db, 'readwrite', (s) => s.clear()); } catch { /* ignore */ }
}

/** localStorage with try/catch (may throw when blocked). */
export const ls = {
  get(key, fallback = null) {
    try { const v = localStorage.getItem(key); return v === null ? fallback : JSON.parse(v); } catch { return fallback; }
  },
  set(key, value) {
    try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* ignore */ }
  },
  del(key) {
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  }
};

export const ss = {
  get(key, fallback = null) {
    try { const v = sessionStorage.getItem(key); return v === null ? fallback : JSON.parse(v); } catch { return fallback; }
  },
  set(key, value) {
    try { sessionStorage.setItem(key, JSON.stringify(value)); } catch { /* ignore */ }
  },
  del(key) {
    try { sessionStorage.removeItem(key); } catch { /* ignore */ }
  }
};
