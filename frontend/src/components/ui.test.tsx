import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Banner, BandBadge, DialogHost, Empty, ErrorBoundary, Explainable, InfoButton, Meter, Toasts, closeAllDialogs, openDialog, toast } from './ui';

describe('Banner, BandBadge and Meter', () => {
  it('shows its content with the kind as a class', () => {
    const { container } = render(<Banner kind="danger" icon="alert" small>Careful</Banner>);
    expect(screen.getByText('Careful')).toBeInTheDocument();
    expect(container.firstChild).toHaveClass('banner', 'danger', 'small');
  });

  it('marks the band so the colour comes from the stylesheet', () => {
    render(<BandBadge band="Critical">Very poor</BandBadge>);
    expect(screen.getByText('Very poor')).toHaveAttribute('data-band', 'Critical');
  });

  it('limits the meter fill to 0-100 %', () => {
    const { container, rerender } = render(<Meter band="Good" value={140} />);
    expect((container.querySelector('.meter > span') as HTMLElement).style.width).toBe('100%');
    rerender(<Meter band="Good" value={-5} />);
    expect((container.querySelector('.meter > span') as HTMLElement).style.width).toBe('0%');
  });

  it('shows an empty state', () => {
    render(<Empty icon="check">Nothing here</Empty>);
    expect(screen.getByText('Nothing here')).toBeInTheDocument();
  });
});

describe('InfoButton and Explainable', () => {
  it('opens the explanation on click without triggering the parent', async () => {
    const open = vi.fn();
    const parent = vi.fn();
    render(<div onClick={parent}><InfoButton onClick={open} label="How is this calculated?" /></div>);
    await userEvent.click(screen.getByRole('button', { name: 'How is this calculated?' }));
    expect(open).toHaveBeenCalledTimes(1);
    expect(parent).not.toHaveBeenCalled();
  });

  it('can be used with the mouse and the keyboard, and says it can be clicked', async () => {
    const open = vi.fn();
    render(<Explainable onOpen={open} hint="Priority">42</Explainable>);
    const el = screen.getByRole('button');
    expect(el).toHaveAttribute('title', expect.stringContaining('Priority'));
    await userEvent.click(el);
    el.focus();
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard(' ');
    expect(open).toHaveBeenCalledTimes(3);
  });
});

describe('dialogs', () => {
  it('opens a modal with title, body and footer, and closes from the footer', async () => {
    const onClose = vi.fn();
    render(<DialogHost />);
    act(() => { openDialog((close) => ({ title: 'Heat score', body: <p>Higher = hotter</p>, footer: <button onClick={() => close('ok')}>Done</button> }), { onClose }); });
    const dialog = document.querySelector('dialog')!;
    expect(dialog).toHaveAttribute('open');
    expect(within(dialog).getByRole('heading', { name: 'Heat score' })).toBeInTheDocument();
    expect(within(dialog).getByText('Higher = hotter')).toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Done' }));
    expect(document.querySelector('dialog')).toBeNull();
    expect(onClose).toHaveBeenCalledWith('ok');
  });

  it('closes with the X button and reports it', async () => {
    const onClose = vi.fn();
    render(<DialogHost />);
    act(() => { openDialog(() => ({ title: 'T', body: 'B' }), { onClose }); });
    await userEvent.click(screen.getByRole('button', { name: /close/i }));
    expect(document.querySelector('dialog')).toBeNull();
    expect(onClose).toHaveBeenCalledWith('x');
  });

  it('can have several open and closes them all', () => {
    render(<DialogHost />);
    act(() => { openDialog(() => ({ title: 'A', body: 'a' })); openDialog(() => ({ title: 'B', body: 'b' })); });
    expect(document.querySelectorAll('dialog')).toHaveLength(2);
    act(() => closeAllDialogs());
    expect(document.querySelectorAll('dialog')).toHaveLength(0);
  });
});

describe('toasts', () => {
  it('shows a message, and an error as an alert', () => {
    render(<Toasts />);
    act(() => { toast('Saved'); toast('Failed', { error: true }); });
    expect(screen.getByText('Saved').closest('.toast')).toHaveAttribute('role', 'status');
    expect(screen.getByText('Failed').closest('.toast')).toHaveAttribute('role', 'alert');
  });

  it('goes away after a while', () => {
    vi.useFakeTimers();
    render(<Toasts />);
    act(() => { toast('Soon gone', { ms: 1000 }); });
    expect(screen.getByText('Soon gone')).toBeInTheDocument();
    act(() => { vi.advanceTimersByTime(1100); });
    expect(screen.queryByText('Soon gone')).toBeNull();
    vi.useRealTimers();
  });
});

describe('ErrorBoundary', () => {
  it('shows a message instead of a blank page when a screen fails', () => {
    const spy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const Broken = () => { throw new Error('map exploded'); };
    render(<ErrorBoundary><Broken /></ErrorBoundary>);
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText('map exploded')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
    spy.mockRestore();
  });

  it('renders its children when nothing fails', () => {
    render(<ErrorBoundary><p>Fine</p></ErrorBoundary>);
    expect(screen.getByText('Fine')).toBeInTheDocument();
  });
});
