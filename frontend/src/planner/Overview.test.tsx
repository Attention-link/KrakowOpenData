import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, NetworkError } from '../lib/api';
import { setApp } from '../lib/store';
import { Overview } from './Overview';
import { usePlanner } from './store';

vi.mock('../lib/api', async (orig) => ({ ...(await orig<typeof import('../lib/api')>()), getGrid: async () => { throw new Error('no grid in this test'); } }));

beforeEach(() => {
  setApp({ lang: 'en', event: 'both', online: true, apiOk: true });
  usePlanner.setState({ summary: null, summaryError: null, reloadSummary: () => {}, grid: null });
});

describe('Overview with no data', () => {
  it('shows placeholders while the first answer is on its way', () => {
    const { container } = render(<Overview />);
    expect(container.querySelectorAll('.skeleton').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
  });

  it('explains that the city data is still being prepared (503) and offers to try again', async () => {
    const reload = vi.fn();
    usePlanner.setState({ summaryError: new ApiError(503, null), reloadSummary: reload });
    render(<Overview />);
    expect(screen.getByText(/still being prepared/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('says so when the API cannot be reached, instead of staying empty', () => {
    usePlanner.setState({ summaryError: new NetworkError('network') });
    render(<Overview />);
    expect(screen.getByText(/offline|connection/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
