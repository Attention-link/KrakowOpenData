import { useCallback, useEffect, useRef, useState } from 'react';
import { cachedGet, type Cached } from './api';

export interface Query<T> {
  data: T | null;
  stale: boolean;
  savedAt: number | null;
  error: unknown;
  loading: boolean;
  reload: () => Promise<void>;
}

interface QueryState<T> { key: string | null; data: T | null; stale: boolean; savedAt: number | null; error: unknown; loading: boolean }

/**
 * Loads data through cachedGet (network first, saved copy when offline). Reloads when `key` changes.
 * `key` doubles as the offline cache key, so include everything the answer depends on. When the key changes the old
 * answer is dropped at once, so a screen never shows data that belongs to the previous key under the new one.
 */
export function useCachedQuery<T>(key: string | null, fetcher: () => Promise<T>, deps: unknown[] = []): Query<T> {
  const [state, setState] = useState<QueryState<T>>({ key, data: null, stale: false, savedAt: null, error: null, loading: key !== null });
  const fetchRef = useRef(fetcher);
  fetchRef.current = fetcher;
  const token = useRef(0);

  const reload = useCallback(async () => {
    if (key === null) return;
    const mine = ++token.current;
    setState((s) => (s.key === key ? { ...s, loading: true } : { key, data: null, stale: false, savedAt: null, error: null, loading: true }));
    try {
      const r: Cached<T> = await cachedGet(key, () => fetchRef.current());
      if (mine === token.current) setState({ key, data: r.data, stale: r.stale, savedAt: r.savedAt, error: null, loading: false });
    } catch (e) {
      if (mine === token.current) setState((s) => ({ ...s, key, error: e, loading: false }));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, ...deps]);

  useEffect(() => { void reload(); }, [reload]);

  // Until the reload for a new key has started, do not hand out the previous key's data.
  const current = state.key === key;
  return {
    data: current ? state.data : null,
    stale: current ? state.stale : false,
    savedAt: current ? state.savedAt : null,
    error: current ? state.error : null,
    loading: current ? state.loading : key !== null,
    reload
  };
}

/** Calls `fn` every `ms` while the page is visible. */
export function useInterval(fn: () => void, ms: number | null) {
  const ref = useRef(fn);
  ref.current = fn;
  useEffect(() => {
    if (ms === null) return;
    const id = setInterval(() => { if (!document.hidden) ref.current(); }, ms);
    return () => clearInterval(id);
  }, [ms]);
}

export function useDebounced<T>(value: T, ms: number): T {
  const [v, setV] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}

export function useMedia(query: string): boolean {
  const [m, setM] = useState(() => window.matchMedia(query).matches);
  useEffect(() => {
    const mq = window.matchMedia(query);
    const on = () => setM(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [query]);
  return m;
}
