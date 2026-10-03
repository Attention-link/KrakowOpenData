// Shapes returned by the API (see KrakowOpenData.Contracts/SafetyDtos.cs). JSON is camelCase.

export type Mode = 'safety' | 'heat' | 'both';
export type EventName = 'heat' | 'night' | 'both';
export type Lang = 'en' | 'pl' | 'uk';
export type Band = 'Good' | 'Fair' | 'Weak' | 'Critical';
export type LayerKey = 'heat' | 'safety';
export type LatLon = [number, number];

export interface GridMeta {
  originLatitude: number;
  originLongitude: number;
  cellLatitudeDegrees: number;
  cellLongitudeDegrees: number;
  cellSizeMeters: number;
  goodFrom: number;
  fairFrom: number;
  weakFrom: number;
  searchRadiusMeters: number;
}

export interface Conditions {
  generatedAt: string;
  isDark: boolean;
  sunriseLocal: string | null;
  sunsetLocal: string | null;
  heat: { pressure: string; level: number; label: string; temperatureC: number | null; warningTitle: string | null };
  air: { band: string; pm25: number | null; station: string | null };
  hydro: { elevatedGauges: number; worstState: string };
  warnings: { eventName: string; level: number; content?: string }[];
  suggestedMode: Mode;
  notes: string[];
  dataGaps: string[];
}

export interface Grid {
  grid: GridMeta;
  event: string;
  columns: string[];
  cells: number[][];
  generatedAt: string;
  conditions: Conditions;
}

export interface Factor {
  key: string;
  label: string;
  layer: string;
  weight: number;
  value: number | null;
  unit: string;
  score: number;
  points: number;
  nearestName: string | null;
  contribution: number;
}

export interface LayerScore {
  score: number;
  band: Band;
  baseScore: number;
  reportPenalty: number;
  factors: Factor[];
}

export interface NearestFeature {
  key: string;
  kind: string;
  name: string | null;
  latitude: number;
  longitude: number;
  distanceMeters: number;
  walkingMinutes: number;
  openingHours: string | null;
}

export interface SuggestedAction {
  code: string;
  layer: string;
  factorKey: string;
  severity: string;
  text: string;
}

export interface Report {
  id: string;
  type: string;
  layer: string;
  latitude: number;
  longitude: number;
  cellId: string;
  createdAt: string;
  lastActivityAt: string;
  supporters: number;
  verifiedByPlanner: boolean;
  status: string;
  note: string | null;
  resolutionNote: string | null;
}

export interface Place {
  cellId: string;
  latitude: number;
  longitude: number;
  heat: LayerScore;
  safety: LayerScore;
  combined: number;
  combinedBand: Band;
  exposure: number;
  priority: number;
  event: string;
  nearest: NearestFeature[];
  reports: Report[];
  actions: SuggestedAction[];
  generatedAt: string;
  label?: string | null;
}

export interface Feature {
  key: string;
  kind: string;
  name: string | null;
  latitude: number;
  longitude: number;
  radiusMeters: number;
  openingHours: string | null;
}

export interface ReportType {
  type: string;
  layer: string;
  weight: number;
  halfLifeHours: number;
  label: string;
}

export interface CorridorSample {
  latitude: number;
  longitude: number;
  heat: number;
  safety: number;
  combined: number;
}

export interface RouteOption {
  kind: string;
  lengthMeters: number;
  walkingMinutes: number;
  path: LatLon[];
  samples: CorridorSample[];
  average: number;
  worst: number;
  weakestSampleIndex: number;
  openReportsNearby: number;
}

export interface Routes {
  mode: string;
  source: 'street' | 'straight-line';
  fastest: RouteOption;
  better: RouteOption | null;
  betterKind: string;
  scoreGain: number;
  extraMeters: number;
  extraMinutes: number;
  note: string;
}

export interface PlannerAlert {
  id: string;
  layer: string | null;
  severity: 'Info' | 'Warning' | 'Critical';
  title: string;
  message: string;
  translations: Record<string, string>;
  latitude: number;
  longitude: number;
  radiusMeters: number;
  createdAt: string;
  expiresAt: string;
  cellId: string | null;
  status: string;
  devicesInArea: number | null;
}

export interface Agency {
  id: string;
  name: string;
  responsibility: string;
  phone: string | null;
  url: string | null;
  contactVerified: boolean;
}

export interface Dispatch {
  id: string;
  agencyId: string;
  agencyName: string;
  subject: string;
  body: string;
  latitude: number | null;
  longitude: number | null;
  cellId: string | null;
  delivery: string;
  reference: string;
  createdAt: string;
}

export interface Kpi {
  key: string;
  value: number;
  unit: string;
}

export interface PriorityCell {
  cellId: string;
  latitude: number;
  longitude: number;
  priority: number;
  heat: number;
  safety: number;
  combined: number;
  exposure: number;
  openReports: number;
  weakFactors: string[];
  actions: SuggestedAction[];
  label?: string | null;
}

export interface PlannerSummary {
  event: EventName;
  generatedAt: string;
  conditions: Conditions;
  cells: number;
  kpis: Kpi[];
  histogram: { from: number; to: number; cells: number }[];
  factorGaps: { key: string; label: string; layer: string; weakShare: number; averageScore: number }[];
  topPriority: PriorityCell[];
  reports: { type: string; open: number; last24Hours: number; verified: number }[];
  activeAlerts: number;
  devicesActive: number;
  notes: string[];
}

export interface GeocodeResult {
  label: string;
  name: string | null;
  street: string | null;
  houseNumber: string | null;
  district: string | null;
  postcode: string | null;
  latitude: number;
  longitude: number;
  kind: string;
}

export interface BandInfo { band: Band; from: number; to: number; meaning: string }

export interface FactorInfo {
  key: string;
  label: string;
  layer: string;
  weight: number;
  measures: string;
  fullScoreAt: string;
  zeroScoreAt: string;
  whyWeighted: string;
  source: string;
  mappedCount: number;
  dataCaveat: string | null;
}

export interface LayerMethod {
  layer: string;
  title: string;
  direction: string;
  meaning: string;
  formula: string;
  bands: BandInfo[];
  factors: FactorInfo[];
}

export interface Method {
  generatedAt: string;
  layers: LayerMethod[];
  combinedFormula: string;
  reportRule: string;
  priorityFormula: string;
  exposureFormula: string;
  temperatureC: number | null;
  heatPressure: string;
  kpis: { key: string; title: string; definition: string; formula: string; source: string }[];
  limits: string[];
}

export interface StopHit {
  name: string;
  code?: string;
  latitude: number;
  longitude: number;
}

/** A pickable search result (address or stop). */
export interface SearchHit {
  kind: 'address' | 'stop';
  label: string;
  lat: number;
  lon: number;
  detail: string | null;
}
