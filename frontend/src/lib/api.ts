// API client + offline cache. Every read goes through cachedGet(): fresh from the network when possible,
// otherwise the last saved answer (marked stale) so screens keep working with no connection.

import { appState, setApp } from './store';
import { kvGet, kvSet } from './storage';
import type {
  Agency, Conditions, Dispatch, EventName, Feature, GeocodeResult, Grid, LatLon, Method, Place, PlannerAlert, PlannerSummary,
  Report, ReportType, Routes, StopHit
} from './types';

declare global { interface Window { KRK_CONFIG?: { apiBase?: string; plannerAutoKey?: string } } }

const cfg = window.KRK_CONFIG || {};
export const API_BASE = (cfg.apiBase || 'http://localhost:5080').replace(/\/$/, '');
export const DOCS_URL = `${API_BASE}/swagger`;
export const PLANNER_AUTO_KEY = cfg.plannerAutoKey || '';

export class ApiError extends Error {
  status: number;
  problem: any;
  retryAfter: number | null = null;
  constructor(status: number, problem: any, message?: string) {
    super(message || problem?.detail || problem?.title || `HTTP ${status}`);
    this.status = status;
    this.problem = problem;
  }
}

export class NetworkError extends Error {}

interface RequestOptions { method?: string; body?: unknown; planner?: boolean; timeout?: number }

/** Low-level request. Throws NetworkError (no connection / timeout) or ApiError (HTTP error). */
export async function request<T = any>(path: string, { method = 'GET', body, planner = false, timeout = 20000 }: RequestOptions = {}): Promise<T> {
  const ctrl = new AbortController();
  const s = appState();
  // When the API already failed, probe quickly instead of making the user wait for a long timeout.
  const timer = setTimeout(() => ctrl.abort(), s.apiOk ? timeout : Math.min(timeout, 3000));
  const headers: Record<string, string> = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (planner && s.plannerKey) headers['X-Planner-Key'] = s.plannerKey;

  let res: Response;
  try {
    res = await fetch(API_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: ctrl.signal });
  } catch (e: any) {
    clearTimeout(timer);
    markApi(false);
    throw new NetworkError(e?.name === 'AbortError' ? 'timeout' : 'network');
  }
  clearTimeout(timer);

  markApi(!(res.status >= 500 && res.status !== 503));

  if (!res.ok) {
    let problem: any = null;
    try { problem = await res.json(); } catch { /* not JSON */ }
    const err = new ApiError(res.status, problem);
    err.retryAfter = Number(res.headers.get('Retry-After')) || null;
    if (res.status === 401 && planner) window.dispatchEvent(new CustomEvent('planner-unauthorized'));
    throw err;
  }
  if (res.status === 204) return null as T;
  return res.json();
}

function markApi(ok: boolean) {
  if (appState().apiOk !== ok) setApp({ apiOk: ok });
}

/** True for errors that mean "the data source is not reachable right now" (use the cache). */
export const isUnavailable = (e: unknown) => e instanceof NetworkError || (e instanceof ApiError && e.status >= 500);

export interface Cached<T> { data: T; savedAt: number; stale: boolean }

/** Network first; on failure the last saved copy (stale: true). Throws when there is neither. */
export async function cachedGet<T>(key: string, fetcher: () => Promise<T>): Promise<Cached<T>> {
  // The browser says there is no network at all: answer from the saved copy at once.
  if (!appState().online) {
    const saved = await kvGet<{ data: T; savedAt: number }>(key);
    if (saved) return { data: saved.data, savedAt: saved.savedAt, stale: true };
  }
  try {
    const data = await fetcher();
    const savedAt = Date.now();
    void kvSet(key, { data, savedAt });
    return { data, savedAt, stale: false };
  } catch (e) {
    if (isUnavailable(e)) {
      const saved = await kvGet<{ data: T; savedAt: number }>(key);
      if (saved) return { data: saved.data, savedAt: saved.savedAt, stale: true };
    }
    throw e;
  }
}

export const peek = <T = any>(key: string) => kvGet<{ data: T; savedAt: number }>(key);

const q = (obj: Record<string, unknown>) => Object.entries(obj).filter(([, v]) => v !== undefined && v !== null && v !== '')
  .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`).join('&');

const ll = (p: LatLon) => `${p[0]},${p[1]}`;

// ── Public endpoints ─────────────────────────────────────────────────────────
export const getConditions = () => request<Conditions>('/api/safety/conditions');
export const getGrid = (event: string) => request<Grid>(`/api/safety/grid?${q({ event })}`, { timeout: 60000 });
export const getPlace = (lat: number, lon: number, event: string) => request<Place>(`/api/safety/place?${q({ lat, lon, event })}`);
export const getCell = (id: string, event: string, planner = false) => request<Place>(`/api/safety/cells/${encodeURIComponent(id)}?${q({ event })}`, { planner });
export const getRoutes = (from: LatLon, to: LatLon, mode: string) => request<Routes>(`/api/safety/route?${q({ from: ll(from), to: ll(to), mode })}`, { timeout: 40000 });
export const getMethod = () => request<Method>('/api/safety/method', { timeout: 30000 });
export const getFeatures = () => request<Feature[]>('/api/safety/features', { timeout: 60000 });
export const getReportTypes = () => request<ReportType[]>('/api/safety/report-types');
export const getReports = (opts: Record<string, unknown> = {}, planner = false) => request<Report[]>(`/api/safety/reports?${q(opts)}`, { planner });
export const postReport = (body: unknown) => request<Report>('/api/safety/reports', { method: 'POST', body });
export const confirmReport = (id: string) => request(`/api/safety/reports/${encodeURIComponent(id)}/confirm`, { method: 'POST', body: { deviceId: appState().deviceId } });
export const getAlerts = (lat: number, lon: number) => request<PlannerAlert[]>(`/api/safety/alerts?${q({ lat, lon, deviceId: appState().deviceId })}`);
export const searchStops = (query: string) => request<{ items: StopHit[] }>(`/api/mobility/stops?${q({ q: query, pageSize: 6 })}`);
export const ping = () => fetch(`${API_BASE}/health`, { cache: 'no-store' }).then((r) => r.ok);

// ── Planner endpoints (X-Planner-Key) ────────────────────────────────────────
const P = <T = any>(path: string, opts: RequestOptions = {}) => request<T>(`/api/safety/planner${path}`, { planner: true, ...opts });
export const plannerPing = () => P('/ping');
export const getSummary = (event: EventName, top = 12) => P<PlannerSummary>(`/summary?${q({ event, top })}`, { timeout: 60000 });
export const verifyReport = (id: string) => P(`/reports/${encodeURIComponent(id)}/verify`, { method: 'POST' });
export const resolveReport = (id: string, note: string | null) => P(`/reports/${encodeURIComponent(id)}/resolve`, { method: 'POST', body: { note } });
export const getPlannerAlerts = (includeInactive = true) => P<PlannerAlert[]>(`/alerts?${q({ includeInactive })}`);
export const createAlert = (body: unknown) => P<PlannerAlert>('/alerts', { method: 'POST', body });
export const cancelAlert = (id: string) => P(`/alerts/${encodeURIComponent(id)}`, { method: 'DELETE' });
export const getReach = (lat: number, lon: number, radius: number) =>
  P<{ devicesInArea: number; devicesActive: number; windowMinutes: number }>(`/reach?${q({ lat, lon, radius })}`);
export const getAgencies = () => P<Agency[]>('/agencies');
export const createDispatch = (body: unknown) => P<Dispatch>('/dispatches', { method: 'POST', body });
export const getDispatches = () => P<Dispatch[]>('/dispatches');
export const seedDemo = () => P<{ created: number }>('/demo-data', { method: 'POST' });

/** A readable message for an error, using the API's validation text when there is one. */
export function errorText(e: unknown, t: (k: string) => string): string {
  if (e instanceof NetworkError) return t('err.offline');
  if (e instanceof ApiError) {
    const first = e.problem?.errors && (Object.values(e.problem.errors).flat() as string[])[0];
    if (first) return first;
    if (e.status === 401) return t('err.unauthorized');
    if (e.status === 429) return e.problem?.detail || t('err.rate');
    if (e.status === 503) return t('err.loading');
    return e.problem?.detail || e.problem?.title || t('err.generic');
  }
  return t('err.generic');
}

// ── Address search (OpenStreetMap via Photon, proxied by the API) ─────────────
export const geoSearch = (text: string, near?: LatLon | null) =>
  request<GeocodeResult[]>(`/api/geo/search?${q({ q: text, lat: near?.[0], lon: near?.[1], limit: 6 })}`, { timeout: 8000 });
export const geoReverse = (lat: number, lon: number) =>
  request<GeocodeResult>(`/api/geo/reverse?${q({ lat: lat.toFixed(5), lon: lon.toFixed(5) })}`, { timeout: 8000 });
