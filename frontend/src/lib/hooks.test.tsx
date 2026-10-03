import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useCachedQuery, useDebounced } from './hooks';
import { NetworkError } from './api';
import { kvClearAll } from './storage';
import { setApp } from './store';

beforeEach(async () => { setApp({ online: true, apiOk: true }); await kvClearAll(); });

describe('useCachedQuery', () => {
  it('loads data and reports loading until it arrives', async () => {
    const { result } = renderHook(() => useCachedQuery('q1', async () => 'hello'));
    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.data).toBe('hello'));
    expect(result.current.loading).toBe(false);
    expect(result.current.stale).toBe(false);
  });

  it('serves the saved copy (stale) when the network fails', async () => {
    const ok = renderHook(() => useCachedQuery('q2', async () => 'saved'));
    await waitFor(() => expect(ok.result.current.data).toBe('saved'));
    ok.unmount();
    const { result } = renderHook(() => useCachedQuery('q2', async () => { throw new NetworkError('network'); }));
    await waitFor(() => expect(result.current.data).toBe('saved'));
    expect(result.current.stale).toBe(true);
  });

  it('reports an error when there is neither a connection nor a saved copy', async () => {
    const { result } = renderHook(() => useCachedQuery('q3', async () => { throw new NetworkError('network'); }));
    await waitFor(() => expect(result.current.error).toBeInstanceOf(NetworkError));
    expect(result.current.data).toBeNull();
  });

  it('never shows the answer of the previous key under the new key', async () => {
    const { result, rerender } = renderHook(({ k }) => useCachedQuery(k, async () => `data for ${k}`), { initialProps: { k: 'heat' } });
    await waitFor(() => expect(result.current.data).toBe('data for heat'));
    rerender({ k: 'night' });
    // Straight after the key changes the old data is gone (not shown as if it were the new answer).
    expect(result.current.data).toBeNull();
    await waitFor(() => expect(result.current.data).toBe('data for night'));
  });

  it('can be reloaded', async () => {
    let n = 0;
    const { result } = renderHook(() => useCachedQuery('q5', async () => ++n));
    await waitFor(() => expect(result.current.data).toBe(1));
    await act(async () => { await result.current.reload(); });
    expect(result.current.data).toBe(2);
  });

  it('ignores a slow answer that arrives after a newer one', async () => {
    const resolvers: ((v: string) => void)[] = [];
    const { result } = renderHook(() => useCachedQuery('q6', () => new Promise<string>((r) => resolvers.push(r))));
    await waitFor(() => expect(resolvers).toHaveLength(1));
    act(() => { void result.current.reload(); });
    await waitFor(() => expect(resolvers).toHaveLength(2));
    await act(async () => { resolvers[1]('new'); });
    await act(async () => { resolvers[0]('old'); });
    expect(result.current.data).toBe('new');
  });
});

describe('useDebounced', () => {
  it('waits before passing a new value on', () => {
    vi.useFakeTimers();
    const { result, rerender } = renderHook(({ v }) => useDebounced(v, 300), { initialProps: { v: 'a' } });
    rerender({ v: 'ab' });
    rerender({ v: 'abc' });
    expect(result.current).toBe('a');
    act(() => { vi.advanceTimersByTime(300); });
    expect(result.current).toBe('abc');
    vi.useRealTimers();
  });
});
