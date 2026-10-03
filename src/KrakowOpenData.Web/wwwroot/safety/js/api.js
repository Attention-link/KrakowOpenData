// API client + offline cache. Every read goes through cachedGet(): fresh from the network when possible,
// otherwise the last saved answer (marked stale) so screens keep working with no connection.

import { state, set, emit } from './state.js';
import { kvGet, kvSet } from './db.js';

const cfg = window.KRK_CONFIG || {};
export const API_BASE = (cfg.apiBase || 'http://localhost:5080').replace(/\/$/, '');
export const DOCS_URL = `${API_BASE}/swagger`;

export class ApiError extends Error {
  constructor(status, problem, message) {
    super(message || problem?.detail || problem?.title || `HTTP ${status}`);
    this.status = status;
    this.problem = problem;
  }
}

export class NetworkError extends Error {}

/** Low-level request. Throws NetworkError (no connection / timeout) or ApiError (HTTP error). */
export async function request(path, { method = 'GET', body, planner = false, timeout = 20000 } = {}) {
  const ctrl = new AbortController();
  // When the API already failed, probe quickly instead of making the user wait for a long timeout.
  const timer = setTimeout(() => ctrl.abort(), state.apiOk ? timeout : Math.min(timeout, 3000));
  const headers = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (planner && state.plannerKey) headers['X-Planner-Key'] = state.plannerKey;

  let res;
  try {
    res = await fetch(API_BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal: ctrl.signal });
  } catch (e) {
    clearTimeout(timer);
    markApi(false);
    throw new NetworkError(e?.name === 'AbortError' ? 'timeout' : 'network');
  }
  clearTimeout(timer);

  if (res.status >= 500 && res.status !== 503) markApi(false);
  else markApi(true);

  if (!res.ok) {
    let problem = null;
    try { problem = await res.json(); } catch { /* not JSON */ }
    const err = new ApiError(res.status, problem);
    err.retryAfter = Number(res.headers.get('Retry-After')) || null;
    if (res.status === 401 && planner) emit('plannerUnauthorized');
    throw err;
  }
  if (res.status === 204) return null;
  return res.json();
}

function markApi(ok) {
  if (state.apiOk !== ok) set({ apiOk: ok });
}

/** True for errors that mean "the data source is not reachable right now" (use the cache). */
export const isUnavailable = (e) => e instanceof NetworkError || (e instanceof ApiError && e.status >= 500);

/**
 * Returns {data, savedAt, stale}. Network first; on failure the last saved copy (stale: true).
 * Throws when there is neither.
 */
export async function cachedGet(key, fetcher) {
  // The browser says there is no network at all: answer from the saved copy at once.
  if (!state.online) {
    const saved = await kvGet(key);
    if (saved) return { data: saved.data, savedAt: saved.savedAt, stale: true };
  }
  try {
    const data = await fetcher();
    const savedAt = Date.now();
    kvSet(key, { data, savedAt });
    return { data, savedAt, stale: false };
  } catch (e) {
    if (isUnavailable(e)) {
      const saved = await kvGet(key);
      if (saved) return { data: saved.data, savedAt: saved.savedAt, stale: true };
    }
    throw e;
  }
}

export const peek = (key) => kvGet(key);

const q = (obj) => Object.entries(obj).filter(([, v]) => v !== undefined && v !== null && v !== '')
  .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');

// ── Public endpoints ─────────────────────────────────────────────────────────
export const getConditions = () => request('/api/safety/conditions');
export const getGrid = (event) => request(`/api/safety/grid?${q({ event })}`, { timeout: 60000 });
export const getPlace = (lat, lon, event) => request(`/api/safety/place?${q({ lat, lon, event })}`);
export const getCell = (id, event, planner = false) => request(`/api/safety/cells/${encodeURIComponent(id)}?${q({ event })}`, { planner });
export const getCorridor = (from, to) => request(`/api/safety/corridor?${q({ from: `${from[0]},${from[1]}`, to: `${to[0]},${to[1]}` })}`);
export const getFeatures = () => request('/api/safety/features', { timeout: 60000 });
export const getReportTypes = () => request('/api/safety/report-types');
export const getReports = (opts = {}, planner = false) => request(`/api/safety/reports?${q(opts)}`, { planner });
export const postReport = (body) => request('/api/safety/reports', { method: 'POST', body });
export const confirmReport = (id) => request(`/api/safety/reports/${encodeURIComponent(id)}/confirm`, { method: 'POST', body: { deviceId: state.deviceId } });
export const getAlerts = (lat, lon) => request(`/api/safety/alerts?${q({ lat, lon, deviceId: state.deviceId })}`);
export const searchStops = (query) => request(`/api/mobility/stops?${q({ q: query, pageSize: 6 })}`);
export const ping = () => fetch(`${API_BASE}/health`, { cache: 'no-store' }).then((r) => r.ok);

// ── Planner endpoints (X-Planner-Key) ────────────────────────────────────────
const P = (path, opts = {}) => request(`/api/safety/planner${path}`, { planner: true, ...opts });
export const plannerPing = () => P('/ping');
export const getSummary = (event, top = 12) => P(`/summary?${q({ event, top })}`, { timeout: 60000 });
export const verifyReport = (id) => P(`/reports/${encodeURIComponent(id)}/verify`, { method: 'POST' });
export const resolveReport = (id, note) => P(`/reports/${encodeURIComponent(id)}/resolve`, { method: 'POST', body: { note } });
export const getPlannerAlerts = (includeInactive = true) => P(`/alerts?${q({ includeInactive })}`);
export const createAlert = (body) => P('/alerts', { method: 'POST', body });
export const cancelAlert = (id) => P(`/alerts/${encodeURIComponent(id)}`, { method: 'DELETE' });
export const getReach = (lat, lon, radius) => P(`/reach?${q({ lat, lon, radius })}`);
export const getAgencies = () => P('/agencies');
export const createDispatch = (body) => P('/dispatches', { method: 'POST', body });
export const getDispatches = () => P('/dispatches');
export const seedDemo = () => P('/demo-data', { method: 'POST' });

/** A readable message for an error, using the API's validation text when there is one. */
export function errorText(e, t) {
  if (e instanceof NetworkError) return t('err.offline');
  if (e instanceof ApiError) {
    const first = e.problem?.errors && Object.values(e.problem.errors).flat()[0];
    if (first) return first;
    if (e.status === 401) return t('err.unauthorized');
    if (e.status === 429) return e.problem?.detail || t('err.rate');
    if (e.status === 503) return t('err.loading');
    return e.problem?.detail || e.problem?.title || t('err.generic');
  }
  return t('err.generic');
}

// ── Address search (OpenStreetMap via Photon, proxied by the API) ─────────────
export const geoSearch = (text, near) => request(`/api/geo/search?${q({ q: text, lat: near?.[0], lon: near?.[1], limit: 6 })}`, { timeout: 8000 });
export const geoReverse = (lat, lon) => request(`/api/geo/reverse?${q({ lat: lat.toFixed(5), lon: lon.toFixed(5) })}`, { timeout: 8000 });

// ── Routes and method (documentation of the scores) ──────────────────────────
/** mode: night | heat | both. Returns the fastest street route and, when clearly better, a safer / cooler one. */
export const getRoutes = (from, to, mode) => request(`/api/safety/route?${q({ from: `${from[0]},${from[1]}`, to: `${to[0]},${to[1]}`, mode })}`, { timeout: 40000 });
export const getMethod = () => request('/api/safety/method', { timeout: 30000 });
