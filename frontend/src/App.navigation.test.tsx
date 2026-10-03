// Navigation between the resident home page and the planner dashboard, with the API replaced by a fake. The planner must
// load the dashboard data the API sends (with the planner key and the planning event), whichever way it is opened.

import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// The host page writes this file (config.js) before the app starts; the demo planner key signs the dashboard in on its own.
vi.hoisted(() => { (window as any).KRK_CONFIG = { apiBase: 'http://api.test', plannerAutoKey: 'demo-planner' }; });

import { App } from './App';
import { kvClearAll } from './lib/storage';
import { setApp } from './lib/store';
import { usePlanner } from './planner/store';
import { closeAllDialogs } from './components/ui';

// ── A small fake API ─────────────────────────────────────────────────────────
const conditions = {
  generatedAt: '2026-10-03T10:00:00Z', isDark: false, sunriseLocal: '06:42', sunsetLocal: '18:15',
  heat: { pressure: 'None', level: 0, label: 'No heat pressure', temperatureC: 21.8, warningTitle: null },
  air: { band: 'Good', pm25: 8, station: 'Kraków, Aleja Krasińskiego' }, hydro: { elevatedGauges: 0, worstState: 'Normal' },
  warnings: [], suggestedMode: 'both', notes: [], dataGaps: []
};
const grid = {
  grid: { originLatitude: 49.9, originLongitude: 19.7, cellLatitudeDegrees: 0.002246, cellLongitudeDegrees: 0.003493, cellSizeMeters: 250, goodFrom: 75, fairFrom: 55, weakFrom: 35, searchRadiusMeters: 1500 },
  event: 'both', columns: ['row', 'col', 'heat', 'safety', 'combined', 'exposure', 'openReports', 'priority'],
  cells: [[40, 70, 80, 30, 30, 90, 0, 70]], generatedAt: '2026-10-03T10:00:00Z', conditions
};
const summary = (event: string, over: Record<string, unknown> = {}) => ({
  event, generatedAt: '2026-10-03T10:00:00Z', conditions, cells: 2720,
  kpis: [
    { key: 'cells', value: 2720, unit: 'cells' }, { key: 'averageScore', value: event === 'heat' ? 61.8 : 41.7, unit: 'score' },
    { key: 'criticalCells', value: 1445, unit: 'cells' }, { key: 'weakCells', value: 545, unit: 'cells' },
    { key: 'noWater500', value: 75.4, unit: '%' }, { key: 'noGreen500', value: 30, unit: '%' }, { key: 'poorlyLit', value: 38.9, unit: '%' },
    { key: 'noNightTransit500', value: 3.9, unit: '%' }, { key: 'openReports', value: 7, unit: 'reports' }
  ],
  histogram: Array.from({ length: 10 }, (_, i) => ({ from: i * 10, to: i * 10 + 10, cells: 10 + i })),
  factorGaps: [{ key: 'water', label: 'Drinking water', layer: 'Heat', weakShare: 70, averageScore: 23.3 }],
  topPriority: [{ cellId: '40-70', latitude: 50.06, longitude: 19.94, priority: 86, heat: 100, safety: 51, combined: 10, exposure: 0.9, openReports: 2, weakFactors: ['water'], actions: [], label: 'Czyżyny Dworzec' }],
  reports: [{ type: 'LightOut', open: 3, last24Hours: 1, verified: 0 }], activeAlerts: 1, devicesActive: 4, notes: [], ...over
});

const seen: { url: string; key: string | null }[] = [];
let plannerStatus = 200;

function api(url: string, init?: RequestInit): Response {
  const u = new URL(url);
  const headers = new Headers(init?.headers);
  seen.push({ url: u.pathname + u.search, key: headers.get('X-Planner-Key') });
  const ok = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
  if (u.pathname.startsWith('/api/safety/planner/')) {
    if (plannerStatus !== 200) return new Response(JSON.stringify({ title: 'x' }), { status: plannerStatus, headers: { 'Content-Type': 'application/json', 'Retry-After': '30' } });
    if (headers.get('X-Planner-Key') !== 'demo-planner') return new Response('{}', { status: 401 });
    if (u.pathname.endsWith('/summary')) return ok(summary(u.searchParams.get('event') || 'both'));
    if (u.pathname.endsWith('/reports')) return ok([]);
    if (u.pathname.endsWith('/alerts')) return ok([]);
    return ok([]);
  }
  switch (u.pathname) {
    case '/api/safety/conditions': return ok(conditions);
    case '/api/safety/grid': return ok(grid);
    case '/api/safety/features': return ok([]);
    case '/api/safety/report-types': return ok([]);
    case '/api/safety/alerts': return ok([]);
    case '/api/safety/method': return ok({ layers: [], kpis: [], limits: [], combinedFormula: '', reportRule: '', priorityFormula: '', exposureFormula: '', temperatureC: null, heatPressure: 'None', generatedAt: '' });
    case '/health': return ok({});
    default: return new Response('{}', { status: 404 });
  }
}

beforeEach(async () => {
  seen.length = 0;
  plannerStatus = 200;
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => api(String(url), init)));
  await kvClearAll();
  setApp({ lang: 'en', online: true, apiOk: true, plannerKey: null, mode: 'both', event: 'both', welcomed: true });
  usePlanner.setState({ summary: null, summaryError: null, dataVersion: 0, drawerCell: null, focus: null, grid: null });
  act(() => closeAllDialogs());
  window.location.hash = '#/';
});

afterEach(() => { vi.unstubAllGlobals(); });

const plannerCalls = () => seen.filter((s) => s.url.startsWith('/api/safety/planner/summary'));

describe('from the home page to the planner dashboard', () => {
  it('shows the resident home page first, with a link to the planner', async () => {
    render(<App />);
    expect(await screen.findByText('Find an address or stop')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /City planner dashboard/ })).toHaveAttribute('href', '#/planner');
    expect(plannerCalls()).toHaveLength(0);   // nothing is asked of the planner API for a resident
  });

  it('loads the dashboard data after following the link', async () => {
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));

    // The dashboard: navigation, then the figures the API sent.
    expect(await screen.findByRole('navigation', { name: /planner/i })).toBeInTheDocument();
    const kpi = await screen.findByRole('button', { name: /Without drinking water/ });
    expect(within(kpi).getByText('75')).toBeInTheDocument();              // 75.4 % shown rounded
    expect(screen.getByRole('button', { name: /Average score/ })).toHaveTextContent('42');
    expect(screen.getByRole('button', { name: /Poorly lit/ })).toHaveTextContent('39');
    expect(screen.getByText('Czyżyny Dworzec', { exact: false })).toBeInTheDocument();   // the top priority area
    expect(window.location.hash).toBe('#/planner/overview');
    expect(screen.queryByText(/Something went wrong/)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
  });

  it('asks the API for the planning event, signed in with the planner key', async () => {
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    await screen.findByRole('button', { name: /Without drinking water/ });
    const calls = plannerCalls();
    expect(calls.length).toBeGreaterThan(0);
    expect(calls.every((c) => c.key === 'demo-planner')).toBe(true);
    expect(calls[0].url).toContain('event=both');
  });

  it('opens the dashboard on the first try, with no stale placeholders left', async () => {
    const { container } = render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    await screen.findByRole('button', { name: /Without drinking water/ });
    expect(container.querySelectorAll('.pl-body .skeleton')).toHaveLength(0);
    expect(container.querySelectorAll('.kpi')).toHaveLength(7);
  });

  it('reloads the figures for another planning event', async () => {
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    await screen.findByRole('button', { name: /Average score/ });
    await userEvent.click(screen.getByRole('button', { name: /Heat warning/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: /Average score/ })).toHaveTextContent('62'));   // the heat summary
    expect(plannerCalls().some((c) => c.url.includes('event=heat'))).toBe(true);
  });

  it('can go to the planner and back to the resident page', async () => {
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    await screen.findByRole('button', { name: /Average score/ });
    await userEvent.click(screen.getByRole('link', { name: /resident/i }));
    expect(await screen.findByText('Find an address or stop')).toBeInTheDocument();
    expect(window.location.hash).toBe('#/');
  });

  it('moves between the dashboard pages and keeps the data', async () => {
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    await screen.findByRole('button', { name: /Average score/ });
    await userEvent.click(screen.getByRole('link', { name: /Reports/ }));
    expect(await screen.findByRole('heading', { name: 'Resident reports' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('link', { name: /Overview/ }));
    expect(await screen.findByRole('button', { name: /Average score/ })).toBeInTheDocument();
  });
});

describe('opening the planner directly', () => {
  it('loads the same dashboard data from a link to #/planner', async () => {
    window.location.hash = '#/planner';
    render(<App />);
    expect(await screen.findByRole('button', { name: /Without drinking water/ })).toBeInTheDocument();
    expect(window.location.hash).toBe('#/planner/overview');
  });

  it('loads a deep link to a dashboard page', async () => {
    window.location.hash = '#/planner/reports';
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Resident reports' })).toBeInTheDocument();
  });

  it('treats an unknown planner page as the overview', async () => {
    window.location.hash = '#/planner/nonsense';
    render(<App />);
    expect(await screen.findByRole('button', { name: /Average score/ })).toBeInTheDocument();
    expect(window.location.hash).toBe('#/planner/overview');
  });
});

describe('when the dashboard data cannot be loaded', () => {
  it('says the data is still being prepared (503), instead of showing nothing', async () => {
    plannerStatus = 503;
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    expect(await screen.findByText(/still being prepared/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('shows the data once the server is ready and the user tries again', async () => {
    plannerStatus = 503;
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: /City planner dashboard/ }));
    await screen.findByText(/still being prepared/);
    plannerStatus = 200;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('button', { name: /Without drinking water/ })).toBeInTheDocument();
  });

  it('asks for the key when the demo key is refused, instead of looping', async () => {
    plannerStatus = 401;
    window.location.hash = '#/planner';
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Planner sign-in' })).toBeInTheDocument();
    const calls = plannerCalls().length;
    await act(async () => { await new Promise((r) => setTimeout(r, 300)); });
    expect(plannerCalls().length).toBe(calls);   // no repeated requests with the refused key
  });
});
