import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { BandKey, Gauge, HBars, Histogram, TableView } from './charts';
import { BAND_FILL } from '../lib/model';

const bins = Array.from({ length: 10 }, (_, i) => ({ from: i * 10, to: i * 10 + 10, cells: i === 9 ? 40 : i }));
const meta = { goodFrom: 75, fairFrom: 55, weakFrom: 35 };

describe('Histogram', () => {
  it('draws ten bars scaled to the biggest one', () => {
    const { container } = render(<Histogram bins={bins} meta={meta} />);
    const fills = [...container.querySelectorAll('.bar .fill')] as HTMLElement[];
    expect(fills).toHaveLength(10);
    expect(fills[9].style.height).toBe('100%');
    expect(parseFloat(fills[4].style.height)).toBeCloseTo(10, 0);
  });

  it('colours safety bars good when high and heat bars good when low', () => {
    const safety = render(<Histogram bins={bins} meta={meta} />).container.querySelectorAll('.bar .fill');
    expect((safety[9] as HTMLElement).style.background).toBe(hex(BAND_FILL.Good));
    expect((safety[0] as HTMLElement).style.background).toBe(hex(BAND_FILL.Critical));
    const heat = render(<Histogram bins={bins} meta={meta} kind="heat" />).container.querySelectorAll('.bar .fill');
    expect((heat[0] as HTMLElement).style.background).toBe(hex(BAND_FILL.Good));
    expect((heat[9] as HTMLElement).style.background).toBe(hex(BAND_FILL.Critical));
  });

  it('turns each bar into a button that explains its range', async () => {
    const onBin = vi.fn();
    render(<Histogram bins={bins} meta={meta} onBin={onBin} />);
    await userEvent.click(screen.getByRole('button', { name: '90–100: 40' }));
    expect(onBin).toHaveBeenCalledWith(bins[9], 'Good');
  });
});

function hex(h: string) {
  // jsdom reports colours as rgb(); compare in that form
  const n = parseInt(h.slice(1), 16);
  return `rgb(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255})`;
}

describe('BandKey', () => {
  it('shows the range of every band, for heat the other way round', () => {
    render(<BandKey meta={meta} kind="heat" />);
    expect(screen.getByText('Low heat 0–25')).toBeInTheDocument();
    expect(screen.getByText('Very high heat 65–100')).toBeInTheDocument();
  });
});

describe('HBars', () => {
  const items = [{ key: 'water', label: 'Drinking water', value: 70, text: '70%', kind: 'heat' }, { key: 'lighting', label: 'Street lighting', value: 40, kind: 'safety' }];

  it('fills each bar to its share', () => {
    const { container } = render(<HBars items={items} />);
    const fills = [...container.querySelectorAll('.fill')] as HTMLElement[];
    expect(fills[0].style.width).toBe('70%');
    expect(fills[1].style.width).toBe('40%');
    expect(screen.getByText('70%')).toBeInTheDocument();
    expect(screen.getByText('40')).toBeInTheDocument();
  });

  it('selects a bar on click and marks it pressed', async () => {
    const onClick = vi.fn();
    render(<HBars items={items} onClick={onClick} selectedKey="water" />);
    expect(screen.getByRole('button', { name: /Drinking water/ })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: /Street lighting/ }));
    expect(onClick).toHaveBeenCalledWith('lighting');
  });

  it('adds a "?" per bar that explains it, separate from selecting it', async () => {
    const onInfo = vi.fn();
    const onClick = vi.fn();
    render(<HBars items={items} onClick={onClick} onInfo={onInfo} />);
    await userEvent.click(screen.getByRole('button', { name: /How is this calculated\?: Drinking water/ }));
    expect(onInfo).toHaveBeenCalledWith('water');
    expect(onClick).not.toHaveBeenCalled();
  });
});

describe('TableView and Gauge', () => {
  it('offers the chart as a table', () => {
    render(<TableView headers={['Range', 'Squares']} rows={[['0–10', 3], ['10–20', 5]]} />);
    expect(screen.getByRole('columnheader', { name: 'Squares' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: '10–20' })).toBeInTheDocument();
  });

  it('shows the score inside the gauge with an accessible label', () => {
    render(<Gauge score={72.4} meta={meta} label="Heat score" kind="heat" />);
    expect(screen.getByRole('img', { name: 'Heat score: 72' })).toBeInTheDocument();
    expect(screen.getByText('72')).toBeInTheDocument();
  });
});
