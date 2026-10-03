import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const geoSearch = vi.fn();
const searchStops = vi.fn();
vi.mock('../lib/api', async (orig) => ({
  ...(await orig<typeof import('../lib/api')>()),
  geoSearch: (...a: unknown[]) => geoSearch(...a),
  searchStops: (...a: unknown[]) => searchStops(...a)
}));

import { LangSelect, SearchBox, StatusPill } from './chrome';
import { setApp, useApp } from '../lib/store';

beforeEach(() => {
  geoSearch.mockReset(); searchStops.mockReset();
  setApp({ online: true, apiOk: true, lang: 'en' });
});

const address = { label: 'Rynek Główny 1, Stare Miasto', name: null, street: null, houseNumber: null, district: null, postcode: '31-042', latitude: 50.0617, longitude: 19.9373, kind: 'house' };

describe('SearchBox', () => {
  it('searches after a short pause and lists addresses and stops, without repeats', async () => {
    geoSearch.mockResolvedValue([address]);
    searchStops.mockResolvedValue({ items: [
      { name: 'Rynek Główny', code: '01', latitude: 50.06, longitude: 19.93 },
      { name: 'Rynek Główny', code: '01', latitude: 50.06, longitude: 19.93 }   // the same stop from a second timetable feed
    ] });
    render(<SearchBox placeholder="Address…" onPick={() => {}} />);
    await userEvent.type(screen.getByRole('combobox'), 'Rynek');
    expect(await screen.findByText('Rynek Główny 1, Stare Miasto')).toBeInTheDocument();
    expect(screen.getAllByText('Rynek Główny 01')).toHaveLength(1);
    expect(geoSearch).toHaveBeenCalledTimes(1);   // one search for the whole word, not one per letter
  });

  it('does not search for a single letter', async () => {
    render(<SearchBox placeholder="Address…" onPick={() => {}} />);
    await userEvent.type(screen.getByRole('combobox'), 'R');
    await act(async () => { await new Promise((r) => setTimeout(r, 450)); });
    expect(geoSearch).not.toHaveBeenCalled();
  });

  it('gives the picked result to the caller and fills in its name without searching again', async () => {
    geoSearch.mockResolvedValue([address]);
    searchStops.mockResolvedValue({ items: [] });
    const onPick = vi.fn();
    render(<SearchBox placeholder="Address…" onPick={onPick} />);
    const input = screen.getByRole('combobox') as HTMLInputElement;
    await userEvent.type(input, 'Rynek');
    await userEvent.click(await screen.findByText('Rynek Główny 1, Stare Miasto'));
    expect(onPick).toHaveBeenCalledWith(expect.objectContaining({ kind: 'address', lat: 50.0617, lon: 19.9373, label: 'Rynek Główny 1, Stare Miasto' }));
    expect(input.value).toBe('Rynek Główny 1, Stare Miasto');
    await act(async () => { await new Promise((r) => setTimeout(r, 450)); });
    expect(geoSearch).toHaveBeenCalledTimes(1);
  });

  it('can be driven with the keyboard', async () => {
    geoSearch.mockResolvedValue([address]);
    searchStops.mockResolvedValue({ items: [] });
    const onPick = vi.fn();
    render(<SearchBox placeholder="Address…" onPick={onPick} />);
    const input = screen.getByRole('combobox');
    await userEvent.type(input, 'Rynek');
    await screen.findByText('Rynek Główny 1, Stare Miasto');
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(onPick).toHaveBeenCalledTimes(1);
  });

  it('filters the results by addresses or stops', async () => {
    geoSearch.mockResolvedValue([address]);
    searchStops.mockResolvedValue({ items: [{ name: 'Rynek Główny', code: '01', latitude: 50.06, longitude: 19.93 }] });
    render(<SearchBox placeholder="Address…" onPick={() => {}} />);
    await userEvent.type(screen.getByRole('combobox'), 'Rynek');
    await screen.findByText('Rynek Główny 1, Stare Miasto');
    await userEvent.click(screen.getByRole('button', { name: 'Stops' }));
    expect(screen.queryByText('Rynek Główny 1, Stare Miasto')).toBeNull();
    expect(screen.getByText('Rynek Główny 01')).toBeInTheDocument();
  });

  it('says so when nothing is found or the search is not available', async () => {
    geoSearch.mockResolvedValue([]);
    searchStops.mockResolvedValue({ items: [] });
    const { unmount } = render(<SearchBox placeholder="Address…" onPick={() => {}} />);
    await userEvent.type(screen.getByRole('combobox'), 'zzzz');
    expect(await screen.findByText(/Nothing found/)).toBeInTheDocument();
    unmount();

    geoSearch.mockRejectedValue(new Error('down'));
    searchStops.mockRejectedValue(new Error('down'));
    render(<SearchBox placeholder="Address…" onPick={() => {}} />);
    await userEvent.type(screen.getByRole('combobox'), 'zzzz');
    expect(await screen.findByText(/not available right now/)).toBeInTheDocument();
  });

  it('is switched off with a hint while offline', () => {
    setApp({ online: false });
    render(<SearchBox placeholder="Address…" onPick={() => {}} />);
    expect(screen.getByRole('combobox')).toBeDisabled();
    expect(screen.getByText(/needs a connection/)).toBeInTheDocument();
  });

  it('shows the start text it was given', () => {
    render(<SearchBox placeholder="Address…" initial="Wawel 5" onPick={() => {}} />);
    expect(screen.getByRole('combobox')).toHaveValue('Wawel 5');
    expect(geoSearch).not.toHaveBeenCalled();
  });
});

describe('StatusPill', () => {
  it('shows online and offline', async () => {
    render(<StatusPill />);
    expect(screen.getByRole('status')).toHaveTextContent('Online');
    act(() => setApp({ apiOk: false }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Offline'));
    expect(screen.getByRole('status')).toHaveClass('off');
  });
});

describe('LangSelect', () => {
  it('changes the language of the whole app and remembers it', async () => {
    render(<LangSelect />);
    await userEvent.selectOptions(screen.getByRole('combobox'), 'pl');
    expect(useApp.getState().lang).toBe('pl');
    expect(JSON.parse(localStorage.getItem('lang')!)).toBe('pl');
  });
});
