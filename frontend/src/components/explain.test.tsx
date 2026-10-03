import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Method, Place } from '../lib/types';

const getMethod = vi.fn();
vi.mock('../lib/api', async (orig) => ({ ...(await orig<typeof import('../lib/api')>()), getMethod: (...a: unknown[]) => getMethod(...a) }));

import { DialogHost, closeAllDialogs } from './ui';
import { LegendBody, bandText, effectText, openFactorExplainer, openKpiExplainer, openMethod, openScoreExplainer, resetMethodCache, scoreSummary } from './explain';
import { setApp } from '../lib/store';
import { kvClearAll } from '../lib/storage';

const method: Method = {
  generatedAt: '2026-10-03T10:00:00Z',
  combinedFormula: 'Overall = 0.6 x the lower + 0.4 x the average.',
  reportRule: 'Each open report subtracts points from its own layer.',
  priorityFormula: 'Priority = (100 - score) x exposure x pressure.',
  exposureFormula: 'Exposure from lamps and stops.',
  temperatureC: 21.8,
  heatPressure: 'None',
  limits: ['Not crime statistics.'],
  kpis: [
    { key: 'noWater500', title: 'No drinking water within 500 m', definition: 'Share of built-up area with no water point within 500 m.', formula: 'Sum of exposure ...', source: 'OpenStreetMap drinking-water points' },
    { key: 'priority', title: 'Priority', definition: 'How urgently a square needs action.', formula: 'Priority formula', source: 'This model' }
  ],
  layers: [
    {
      layer: 'Heat', title: 'Heat score', direction: 'Higher = hotter. 0 is cool; 100 has no relief.', meaning: 'Heat stress on a hot day.', formula: 'Heat = sum of weight x (100 - factor score) / 100.',
      bands: [
        { band: 'Good', from: 0, to: 25, meaning: 'Low heat stress.' }, { band: 'Fair', from: 25, to: 45, meaning: 'Moderate.' },
        { band: 'Weak', from: 45, to: 65, meaning: 'High.' }, { band: 'Critical', from: 65, to: 100, meaning: 'Very high.' }
      ],
      factors: [
        { key: 'green', label: 'Parks and shade', layer: 'Heat', weight: 35, measures: 'Distance to the nearest park.', fullScoreAt: '100 m', zeroScoreAt: '600 m', whyWeighted: 'Shade is the biggest difference on a hot day.', source: 'OpenStreetMap parks', mappedCount: 279, dataCaveat: 'No tree canopy data.' },
        { key: 'water', label: 'Drinking water', layer: 'Heat', weight: 25, measures: 'Distance to drinking water.', fullScoreAt: '150 m', zeroScoreAt: '800 m', whyWeighted: 'A person can carry a bottle.', source: 'OpenStreetMap drinking water', mappedCount: 75, dataCaveat: null }
      ]
    },
    {
      layer: 'Safety', title: 'Night safety score', direction: 'Higher = safer.', meaning: 'Night set-up.', formula: 'Safety = sum of weight x score / 100.',
      bands: [
        { band: 'Good', from: 75, to: 100, meaning: 'Safe set-up.' }, { band: 'Fair', from: 55, to: 75, meaning: 'Mostly fine.' },
        { band: 'Weak', from: 35, to: 55, meaning: 'Poor.' }, { band: 'Critical', from: 0, to: 35, meaning: 'Very poor.' }
      ],
      factors: [{ key: 'lighting', label: 'Street lighting', layer: 'Safety', weight: 40, measures: 'Lamps per km2.', fullScoreAt: '400 lamps per km2', zeroScoreAt: '40 lamps per km2', whyWeighted: 'Light matters most at night.', source: 'OpenStreetMap lamps', mappedCount: 26842, dataCaveat: null }]
    }
  ]
};

const layer = (score: number, band: any, reportPenalty = 0) => ({
  score, band, baseScore: score - reportPenalty, reportPenalty,
  factors: [
    { key: 'green', label: 'Parks and shade', layer: 'Heat', weight: 35, value: null, unit: 'm', score: 0, points: 0, nearestName: null, contribution: 35 },
    { key: 'water', label: 'Drinking water', layer: 'Heat', weight: 25, value: 100, unit: 'm', score: 100, points: 25, nearestName: 'Fountain', contribution: 0 }
  ]
});
const place = { cellId: '1-2', latitude: 50.06, longitude: 19.93, heat: layer(35, 'Weak'), safety: { ...layer(80, 'Good'), factors: [{ key: 'lighting', label: 'Street lighting', layer: 'Safety', weight: 40, value: 450, unit: 'lamps/km²', score: 100, points: 40, nearestName: null, contribution: 40 }] }, combined: 60, combinedBand: 'Fair' } as unknown as Place;

beforeEach(async () => {
  getMethod.mockReset();
  getMethod.mockResolvedValue(method);
  resetMethodCache();
  setApp({ online: true, apiOk: true, lang: 'en' });
  await kvClearAll();
  act(() => closeAllDialogs());   // dialogs live in a store that outlives a single test
  render(<DialogHost />);
});

const dialog = async () => within(await waitFor(() => { const d = document.querySelector('dialog'); if (!d) throw new Error('no dialog'); return d as HTMLElement; }));

describe('wording helpers', () => {
  it('uses heat words for the heat score and Good-to-Critical for the others', () => {
    expect(bandText('Good', 'heat')).toBe('Low heat');
    expect(bandText('Critical', 'heat')).toBe('Very high heat');
    expect(bandText('Good')).toBe('Good');
  });

  it('describes a score with its number, band and direction', () => {
    expect(scoreSummary('heat', 72.4, 'Critical')).toBe('72 / 100 · Very high heat · Higher = hotter');
    expect(scoreSummary('safety', 80, 'Good')).toBe('80 / 100 · Good · Higher = safer');
  });

  it('says what a factor did to the score', () => {
    expect(effectText('heat', { contribution: 35, weight: 35 })).toBe('adds 35 points of heat');
    expect(effectText('heat', { contribution: 0, weight: 25 })).toBe('adds no heat (relief is close)');
    expect(effectText('safety', { contribution: 30, weight: 40 })).toBe('30 of 40 possible safety points');
  });
});

describe('LegendBody', () => {
  it('shows the heat ranges the right way round: 0-25 is low heat', () => {
    const { container } = render(<LegendBody mode="heat" meta={{ goodFrom: 75, fairFrom: 55, weakFrom: 35 } as any} />);
    const rows = [...container.querySelectorAll('.legend-rows li')].map((li) => li.textContent);
    expect(rows[0]).toContain('0–25');
    expect(rows[0]).toContain('Low heat');
    expect(rows[3]).toContain('65–100');
    expect(rows[3]).toContain('Very high heat');
  });

  it('shows the safety ranges with 75-100 as good', () => {
    const { container } = render(<LegendBody mode="safety" />);
    const rows = [...container.querySelectorAll('.legend-rows li')].map((li) => li.textContent);
    expect(rows[0]).toContain('75–100');
    expect(rows[0]).toContain('Good');
    expect(container.textContent).toContain('Higher = safer');
  });

  it('explains the overall score and adds a note when both are shown', () => {
    const { container } = render(<LegendBody mode="both" />);
    expect(container.textContent).toContain('Overall');
    expect(container.textContent).toContain('weaker of heat relief and night safety');
  });

  it('leaves out the long meanings in the compact legend, and the ? asks for an explanation', async () => {
    const onExplain = vi.fn();
    const { container } = render(<LegendBody mode="heat" compact onExplain={onExplain} />);
    expect(container.textContent).not.toContain('Shade, water and indoor refuge are close.');
    await userEvent.click(screen.getByRole('button', { name: /how is this calculated/i }));
    expect(onExplain).toHaveBeenCalledWith('heat');
  });
});

describe('score explainer', () => {
  it('explains the heat score: direction, scale and the formula', async () => {
    act(() => openScoreExplainer({ layer: 'heat' }));
    const d = await dialog();
    expect(await d.findByText(/Higher = hotter\. 0 is cool/)).toBeInTheDocument();
    expect(d.getByText(/Low heat stress./)).toBeInTheDocument();
    expect(d.getByText(/Heat = sum of weight/)).toBeInTheDocument();
    expect(d.getByText(/Right now it is 21\.8 °C/)).toBeInTheDocument();
  });

  it('explains why a place scored what it did, factor by factor', async () => {
    act(() => openScoreExplainer({ layer: 'heat', place }));
    const d = await dialog();
    expect(await d.findByText(/Biggest contributors to the heat: Parks and shade \(\+35\)/)).toBeInTheDocument();
    const rows = d.getAllByRole('row');
    const green = rows.find((r) => r.textContent?.includes('Parks and shade'))!;
    expect(green.textContent).toContain('35');          // weight and the heat it adds
    expect(green.textContent).toContain('279');         // how many parks are mapped in Kraków
    const water = rows.find((r) => r.textContent?.includes('Drinking water'))!;
    expect(water.textContent).toContain('+0');
  });

  it('lists report points as added heat for heat and subtracted for safety', async () => {
    const withReports = { ...place, heat: layer(45, 'Fair', 10) } as unknown as Place;
    act(() => openScoreExplainer({ layer: 'heat', place: withReports }));
    const d = await dialog();
    expect(await d.findByText('Open resident reports (raise the heat)')).toBeInTheDocument();
  });

  it('shows the overall score with its formula for both views', async () => {
    act(() => openScoreExplainer({ layer: 'both', place }));
    const d = await dialog();
    expect(await d.findByText(/Overall = 0\.6 x the lower/)).toBeInTheDocument();
    expect(d.getByText('Heat score')).toBeInTheDocument();
    expect(d.getByText('Night safety score')).toBeInTheDocument();
  });

  it('says so when the explanation cannot be loaded', async () => {
    resetMethodCache();
    getMethod.mockRejectedValue(new Error('down'));
    act(() => openScoreExplainer({ layer: 'heat' }));
    const d = await dialog();
    expect(await d.findByText(/could not be loaded/)).toBeInTheDocument();
  });
});

describe('factor explainer', () => {
  it('shows the weight, why, source, how many are mapped, and the data caveat', async () => {
    act(() => openFactorExplainer('green', { factor: place.heat.factors[0], layer: 'heat' }));
    const d = await dialog();
    expect(await d.findByText('35%')).toBeInTheDocument();
    expect(d.getByText('Shade is the biggest difference on a hot day.')).toBeInTheDocument();
    expect(d.getByText('OpenStreetMap parks')).toBeInTheDocument();
    expect(d.getByText('279')).toBeInTheDocument();
    expect(d.getByText('No tree canopy data.')).toBeInTheDocument();
    expect(d.getByText('adds 35 points of heat')).toBeInTheDocument();
  });

  it('counts street lamps as lamps', async () => {
    act(() => openFactorExplainer('lighting'));
    const d = await dialog();
    expect(await d.findByText('26,842 lamps')).toBeInTheDocument();
  });
});

describe('full method and dashboard figures', () => {
  it('lists every factor with its weight for both scores', async () => {
    act(() => openMethod());
    const d = await dialog();
    expect(await d.findByText('How the scores are built')).toBeInTheDocument();
    for (const text of ['Parks and shade', 'Drinking water', 'Street lighting']) expect(d.getByText(text)).toBeInTheDocument();
    expect(d.getByText('Shade is the biggest difference on a hot day.')).toBeInTheDocument();
    expect(d.getByText(/Priority = \(100 - score\)/)).toBeInTheDocument();
  });

  it('explains a dashboard figure with its definition, formula, source and live numbers', async () => {
    act(() => openKpiExplainer('noWater500', { value: 75, unit: '%', factorKey: 'water', extra: [['Squares scored', 2720]] }));
    const d = await dialog();
    expect(await d.findByText('75%')).toBeInTheDocument();
    expect(d.getByText(/Share of built-up area with no water point/)).toBeInTheDocument();
    expect(d.getByText(/OpenStreetMap drinking-water points/)).toBeInTheDocument();
    expect(d.getByText('2720')).toBeInTheDocument();
    expect(d.getByText('A person can carry a bottle.')).toBeInTheDocument();   // why water has this weight
  });

  it('offers links that close the dialog', async () => {
    act(() => openKpiExplainer('priority', { links: [{ href: '#/planner/map', label: 'Open map' }] }));
    const d = await dialog();
    await userEvent.click(await d.findByRole('link', { name: 'Open map' }));
    expect(document.querySelector('dialog')).toBeNull();
  });

  afterAllClose();
});

function afterAllClose() {
  // keep the dialog host clean between files
  it('cleans up', () => { act(() => closeAllDialogs()); expect(document.querySelectorAll('dialog')).toHaveLength(0); });
}
