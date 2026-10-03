// Browser storage with try/catch (it can be blocked) and a small IndexedDB key-value store for the offline cache.
// The IndexedDB database keeps the name of the earlier app version, so saved data survives the move to React.

import { createStore, get, set, del, clear } from 'idb-keyval';

let store: ReturnType<typeof createStore> | null = null;
const memory = new Map<string, unknown>();

function db() {
  if (store) return store;
  try {
    store = createStore('krk-safety', 'kv');
  } catch {
    store = null;
  }
  return store;
}

export async function kvGet<T = any>(key: string): Promise<T | undefined> {
  const s = db();
  if (!s) return memory.get(key) as T | undefined;
  try { return await get<T>(key, s); } catch { return memory.get(key) as T | undefined; }
}

export async function kvSet(key: string, value: unknown): Promise<void> {
  const s = db();
  if (!s) { memory.set(key, value); return; }
  try { await set(key, value, s); } catch { memory.set(key, value); }
}

export async function kvDel(key: string): Promise<void> {
  memory.delete(key);
  const s = db();
  if (!s) return;
  try { await del(key, s); } catch { /* ignore */ }
}

export async function kvClearAll(): Promise<void> {
  memory.clear();
  const s = db();
  if (!s) return;
  try { await clear(s); } catch { /* ignore */ }
}

function wrap(storage: () => Storage) {
  return {
    get<T = any>(key: string, fallback: T | null = null): T | null {
      try { const v = storage().getItem(key); return v === null ? fallback : (JSON.parse(v) as T); } catch { return fallback; }
    },
    set(key: string, value: unknown) {
      try { storage().setItem(key, JSON.stringify(value)); } catch { /* ignore */ }
    },
    del(key: string) {
      try { storage().removeItem(key); } catch { /* ignore */ }
    }
  };
}

export const ls = wrap(() => localStorage);
export const ss = wrap(() => sessionStorage);
