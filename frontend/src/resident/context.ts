import { createContext, useContext } from 'react';
import type L from 'leaflet';
import type { Feature, Grid, LatLon, Mode, Place, ReportType } from '../lib/types';

export type View = 'home' | 'place' | 'report' | 'walk';
export type Sheet = 'peek' | 'half' | 'full';

export interface Selected {
  lat: number;
  lon: number;
  row?: number;
  col?: number;
  label: string | null;
  place: Place | null;
  loading: boolean;
  error: unknown;
  stale: boolean;
  savedAt: number | null;
}

export interface ViewProps { to?: LatLon; toLabel?: string | null }

export interface ResidentCtx {
  map: L.Map;
  mode: Mode;
  grid: Grid | null;
  gridIndex: Map<string, number[]>;
  features: Feature[] | null;
  reportTypes: ReportType[] | null;
  selected: Selected | null;
  view: View;
  viewProps: ViewProps | null;
  showView: (view: View, props?: ViewProps | null) => void;
  selectPoint: (lat: number, lon: number, opts?: { fly?: boolean; keepView?: boolean; label?: string | null }) => void;
  refreshGrid: () => void;
  setSheet: (s: Sheet) => void;
  /** A view can claim map clicks (picking a point). Return true when the click was used. */
  registerMapClick: (fn: ((p: LatLon) => boolean) | null) => void;
  locateMe: (asArea?: boolean) => void;
  refreshAlerts: () => void;
  alertsLastChecked: number | null;
}

export const ResidentContext = createContext<ResidentCtx | null>(null);

export function useResident(): ResidentCtx {
  const c = useContext(ResidentContext);
  if (!c) throw new Error('useResident must be used inside <ResidentApp>');
  return c;
}

/** Which score layers a view shows. Heat and Night safety never mix. */
export const layersOf = (mode: Mode) => (mode === 'heat' ? (['heat'] as const) : mode === 'safety' ? (['safety'] as const) : (['safety', 'heat'] as const));
