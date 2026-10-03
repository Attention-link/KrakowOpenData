// Planner state shared by every page and dialog: the planning event, the latest summary, a "data changed" counter that makes
// pages reload, a place for the map to focus on, and the open area drawer.

import { create } from 'zustand';
import { cachedGet, getAgencies, getGrid } from '../lib/api';
import { appState, isOffline, setApp } from '../lib/store';
import { t } from '../lib/i18n';
import { toast } from '../components/ui';
import type { EventName, Grid, LatLon, PlannerSummary } from '../lib/types';

export interface Focus { lat: number; lon: number; cellId?: string }
export interface SummaryState { data: PlannerSummary; stale: boolean; savedAt: number }

interface PlannerState {
  summary: SummaryState | null;
  /** Why there is no summary (still loading on the server, offline, ...) and how to try again. */
  summaryError: unknown;
  reloadSummary: () => void;
  dataVersion: number;
  focus: Focus | null;
  drawerCell: string | null;
  grid: Grid | null;
  setSummary: (s: SummaryState | null) => void;
  setSummaryError: (error: unknown, reload: () => void) => void;
  setFocus: (f: Focus | null) => void;
  openCell: (id: string | null) => void;
  setGrid: (g: Grid | null) => void;
  /** After any change on the server (report verified, alert sent, ...): tell every open page to reload. */
  dataChanged: () => void;
}

export const usePlanner = create<PlannerState>((set) => ({
  summary: null,
  dataVersion: 0,
  focus: null,
  drawerCell: null,
  grid: null,
  summaryError: null,
  reloadSummary: () => {},
  setSummary: (summary) => set({ summary }),
  setSummaryError: (summaryError, reloadSummary) => set({ summaryError, reloadSummary }),
  setFocus: (focus) => set({ focus }),
  openCell: (drawerCell) => set({ drawerCell }),
  setGrid: (grid) => set({ grid }),
  dataChanged: () => set((s) => ({ dataVersion: s.dataVersion + 1 }))
}));

export const plannerEvent = (): EventName => appState().event;
export const changeEvent = (event: EventName) => { if (appState().event !== event) setApp({ event }); };

/** The map focuses on a place when it opens (from a report, an alert or the area drawer). */
export function goToMap(lat: number, lon: number, cellId?: string) {
  usePlanner.getState().setFocus({ lat, lon, cellId });
  location.hash = '#/planner/map';
}

/** Actions that change the server need a connection. */
export function requireOnline(): boolean {
  if (!isOffline()) return true;
  toast(t('pl.needsOnline'), { error: true });
  return false;
}

// ── Loaders (network first, saved copy when offline) ─────────────────────────
export const loadGridFor = (event: EventName) => cachedGet(`p:grid:${event}`, () => getGrid(event));
export const loadAgencies = () => cachedGet('p:agencies', getAgencies);

export type { LatLon };
