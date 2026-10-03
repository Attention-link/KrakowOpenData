import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, NetworkError, cachedGet, errorText, getRoutes, request } from './api';
import { kvClearAll } from './storage';
import { setApp } from './store';

const json = (body: unknown, init: ResponseInit = {}) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' }, ...init });
const t = (k: string) => k;

beforeEach(async () => { setApp({ online: true, apiOk: true, plannerKey: null }); await kvClearAll(); });
afterEach(() => { vi.unstubAllGlobals(); });

describe('request', () => {
  it('returns the JSON body and marks the API as answering', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ ok: 1 })));
    setApp({ apiOk: false });
    expect(await request('/x')).toEqual({ ok: 1 });
    expect((await import('./store')).appState().apiOk).toBe(true);
  });

  it('turns a failed fetch into a NetworkError and marks the API as down', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('failed'); }));
    await expect(request('/x')).rejects.toBeInstanceOf(NetworkError);
    expect((await import('./store')).appState().apiOk).toBe(false);
  });

  it('turns an error response into an ApiError with the problem and Retry-After', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ title: 'Loading' }, { status: 503, headers: { 'Retry-After': '20' } })));
    const err = await request('/x').catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(503);
    expect(err.retryAfter).toBe(20);
    // A 503 means "still loading", not "the API is down".
    expect((await import('./store')).appState().apiOk).toBe(true);
  });

  it('marks the API as down on a 500', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, { status: 500 })));
    await request('/x').catch(() => {});
    expect((await import('./store')).appState().apiOk).toBe(false);
  });

  it('sends the planner key only to planner endpoints', async () => {
    const fetchMock = vi.fn(async () => json({}));
    vi.stubGlobal('fetch', fetchMock);
    setApp({ plannerKey: 'k1' });
    await request('/a', { planner: true });
    await request('/b');
    expect((fetchMock.mock.calls[0] as any)[1].headers['X-Planner-Key']).toBe('k1');
    expect((fetchMock.mock.calls[1] as any)[1].headers['X-Planner-Key']).toBeUndefined();
  });

  it('announces a refused planner key', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({}, { status: 401 })));
    const heard = vi.fn();
    window.addEventListener('planner-unauthorized', heard);
    await request('/p', { planner: true }).catch(() => {});
    window.removeEventListener('planner-unauthorized', heard);
    expect(heard).toHaveBeenCalledTimes(1);
  });

  it('posts JSON bodies', async () => {
    const fetchMock = vi.fn(async () => json({ id: 'r1' }, { status: 201 }));
    vi.stubGlobal('fetch', fetchMock);
    await request('/reports', { method: 'POST', body: { type: 'LightOut' } });
    const init = (fetchMock.mock.calls[0] as any)[1];
    expect(init.method).toBe('POST');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(init.body)).toEqual({ type: 'LightOut' });
  });
});

describe('cachedGet', () => {
  it('returns fresh data and keeps a copy', async () => {
    const r = await cachedGet('k1', async () => ({ v: 1 }));
    expect(r).toMatchObject({ data: { v: 1 }, stale: false });
    // Now the source fails with a 500: the saved copy is served and marked stale.
    const again = await cachedGet('k1', async () => { throw new ApiError(500, null); });
    expect(again).toMatchObject({ data: { v: 1 }, stale: true });
  });

  it('uses the saved copy when there is no connection', async () => {
    await cachedGet('k2', async () => ({ v: 2 }));
    const again = await cachedGet('k2', async () => { throw new NetworkError('network'); });
    expect(again.stale).toBe(true);
    expect(again.data).toEqual({ v: 2 });
  });

  it('answers from the saved copy at once when the browser says it is offline', async () => {
    await cachedGet('k3', async () => ({ v: 3 }));
    setApp({ online: false });
    const fetcher = vi.fn(async () => ({ v: 99 }));
    const r = await cachedGet('k3', fetcher);
    expect(r.data).toEqual({ v: 3 });
    expect(fetcher).not.toHaveBeenCalled();
  });

  it('does not hide a real error (for example a 400) behind a saved copy', async () => {
    await cachedGet('k4', async () => ({ v: 4 }));
    await expect(cachedGet('k4', async () => { throw new ApiError(400, { title: 'bad' }); })).rejects.toBeInstanceOf(ApiError);
  });

  it('throws when there is neither a connection nor a saved copy', async () => {
    await expect(cachedGet('never-saved', async () => { throw new NetworkError('network'); })).rejects.toBeInstanceOf(NetworkError);
  });
});

describe('errorText', () => {
  it('prefers the validation message of the API', () => {
    expect(errorText(new ApiError(400, { errors: { to: ['Routes are limited to 5 km.'] } }), t)).toBe('Routes are limited to 5 km.');
  });
  it('maps the common cases to texts', () => {
    expect(errorText(new NetworkError('x'), t)).toBe('err.offline');
    expect(errorText(new ApiError(401, null), t)).toBe('err.unauthorized');
    expect(errorText(new ApiError(503, null), t)).toBe('err.loading');
    expect(errorText(new ApiError(429, { detail: 'Too many reports' }), t)).toBe('Too many reports');
    expect(errorText(new Error('boom'), t)).toBe('err.generic');
  });
});

describe('getRoutes', () => {
  it('asks for the two points and the mode', async () => {
    const fetchMock = vi.fn(async () => json({ source: 'street' }));
    vi.stubGlobal('fetch', fetchMock);
    await getRoutes([50.06, 19.93], [50.07, 19.95], 'night');
    const url = String((fetchMock.mock.calls[0] as any)[0]);
    expect(url).toContain('/api/safety/route?');
    expect(decodeURIComponent(url)).toContain('from=50.06,19.93');
    expect(decodeURIComponent(url)).toContain('to=50.07,19.95');
    expect(url).toContain('mode=night');
  });
});
