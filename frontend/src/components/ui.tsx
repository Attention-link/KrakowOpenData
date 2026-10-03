// Small shared UI pieces: icons, banners, band badges, meters, "?" buttons, dialogs and toasts.

import { Component, useEffect, useRef, type ReactNode } from 'react';
import { create } from 'zustand';
import { ICONS } from './icons';
import { useT } from '../lib/i18n';
import { announce, clamp } from '../lib/util';
import type { Band } from '../lib/types';

// ── Icon ─────────────────────────────────────────────────────────────────────
export function Icon({ name, size }: { name: string; size?: 'sm' | 'lg' }) {
  return <svg className={`i ${size ?? ''}`.trim()} viewBox="0 0 24 24" aria-hidden="true" dangerouslySetInnerHTML={{ __html: ICONS[name] ?? '' }} />;
}

// ── Banner, badges, meters ───────────────────────────────────────────────────
export type BannerKind = '' | 'info' | 'warn' | 'danger' | 'ok' | 'critical';

export function Banner({ kind = '', icon, small, children, role }: { kind?: BannerKind; icon?: string; small?: boolean; children: ReactNode; role?: string }) {
  return (
    <div className={`banner ${kind} ${small ? 'small' : ''}`.trim()} role={role}>
      {icon && <Icon name={icon} size={small ? 'sm' : undefined} />}
      <div className="grow">{children}</div>
    </div>
  );
}

export function BandBadge({ band, children, className = '' }: { band: Band; children: ReactNode; className?: string }) {
  return <span className={`band ${className}`.trim()} data-band={band}>{children}</span>;
}

export function Meter({ band, value }: { band: Band; value: number }) {
  return <div className="meter" data-band={band} role="presentation"><span style={{ width: `${clamp(value, 0, 100)}%` }} /></div>;
}

export function Skeleton({ height = 80 }: { height?: number }) {
  return <div className="skeleton" style={{ height }} />;
}

export function Empty({ icon, children }: { icon: string; children: ReactNode }) {
  return <div className="empty"><Icon name={icon} /><p>{children}</p></div>;
}

/** A round "?" button that opens an explanation. */
export function InfoButton({ onClick, label }: { onClick: () => void; label?: string }) {
  const t = useT();
  return (
    <button className="info-btn" type="button" title={t('explain.click')} aria-label={label || t('explain.how')}
      onClick={(e) => { e.stopPropagation(); onClick(); }}>?</button>
  );
}

/** Makes its content clickable (and keyboard operable) as an explanation trigger, with a hover hint. */
export function Explainable({ onOpen, hint, children, as: Tag = 'span', className = '' }: { onOpen: () => void; hint?: string; children: ReactNode; as?: 'span' | 'div' | 'p'; className?: string }) {
  const t = useT();
  const title = hint ? `${hint} · ${t('explain.click')}` : t('explain.click');
  return (
    <Tag className={`explainable ${className}`.trim()} role="button" tabIndex={0} title={title}
      onClick={(e: React.MouseEvent) => { e.stopPropagation(); onOpen(); }}
      onKeyDown={(e: React.KeyboardEvent) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); e.stopPropagation(); onOpen(); } }}>
      {children}
    </Tag>
  );
}

export function Field({ label, hint, grow, htmlFor, children }: { label?: ReactNode; hint?: ReactNode; grow?: boolean; htmlFor?: string; children: ReactNode }) {
  return (
    <label className={`field ${grow ? 'grow' : ''}`.trim()} htmlFor={htmlFor}>
      {label && <span>{label}</span>}
      {children}
      {hint && <span className="hint">{hint}</span>}
    </label>
  );
}

// ── Dialogs (opened from anywhere with openDialog) ──────────────────────────
export interface DialogSpec { title: ReactNode; body: ReactNode; footer?: ReactNode }
type Builder = (close: (value?: string) => void) => DialogSpec;
interface DialogEntry { id: number; spec: DialogSpec; onClose?: (value: string) => void }

let nextId = 1;
const useDialogs = create<{ list: DialogEntry[] }>(() => ({ list: [] }));

/** Opens a modal dialog. `build(close)` returns the title, body and footer. Returns {close}. */
export function openDialog(build: Builder, opts: { onClose?: (value: string) => void } = {}) {
  const id = nextId++;
  const close = (value = 'ok') => {
    const entry = useDialogs.getState().list.find((d) => d.id === id);
    if (!entry) return;
    useDialogs.setState((s) => ({ list: s.list.filter((d) => d.id !== id) }));
    entry.onClose?.(value);
  };
  useDialogs.setState((s) => ({ list: [...s.list, { id, spec: build(close), onClose: opts.onClose }] }));
  return { close };
}

/** Closes every open dialog (used before navigating). */
export function closeAllDialogs() {
  useDialogs.setState({ list: [] });
}

function DialogView({ entry }: { entry: DialogEntry }) {
  const ref = useRef<HTMLDialogElement>(null);
  const t = useT();
  useEffect(() => {
    const d = ref.current;
    if (d && !d.open) d.showModal();
  }, []);
  const dismiss = () => {
    useDialogs.setState((s) => ({ list: s.list.filter((x) => x.id !== entry.id) }));
    entry.onClose?.('x');
  };
  return (
    <dialog ref={ref} onCancel={(e) => { e.preventDefault(); dismiss(); }} onClick={(e) => { if (e.target === ref.current) dismiss(); }}>
      <div className="dlg-head">
        <h2 className="grow">{entry.spec.title}</h2>
        <button className="btn icon quiet" type="button" aria-label={t('common.close')} onClick={dismiss}><Icon name="x" /></button>
      </div>
      <div className="dlg-body">{entry.spec.body}</div>
      {entry.spec.footer && <div className="dlg-foot">{entry.spec.footer}</div>}
    </dialog>
  );
}

export function DialogHost() {
  const list = useDialogs((s) => s.list);
  return <>{list.map((d) => <DialogView key={d.id} entry={d} />)}</>;
}

// ── Error boundary: a failing screen shows a message and a retry instead of a blank page ──
interface BoundaryState { error: Error | null }

export class ErrorBoundary extends Component<{ children: ReactNode }, BoundaryState> {
  state: BoundaryState = { error: null };
  static getDerivedStateFromError(error: Error): BoundaryState { return { error }; }
  componentDidCatch(error: Error) { console.error(error); }
  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="login">
        <div className="card stack">
          <h1>Something went wrong</h1>
          <p className="muted">The screen could not be shown. Your saved reports and settings are safe on this device.</p>
          <p className="tiny muted">{this.state.error.message}</p>
          <button className="btn primary" type="button" onClick={() => { this.setState({ error: null }); location.reload(); }}>Reload</button>
        </div>
      </div>
    );
  }
}

// ── Toasts ───────────────────────────────────────────────────────────────────
interface ToastEntry { id: number; message: string; error?: boolean; action?: { label: string; onClick: () => void } }
const useToasts = create<{ list: ToastEntry[] }>(() => ({ list: [] }));
let toastId = 1;

export function toast(message: string, opts: { error?: boolean; ms?: number; action?: ToastEntry['action'] } = {}) {
  const id = toastId++;
  useToasts.setState((s) => ({ list: [...s.list, { id, message, error: opts.error, action: opts.action }] }));
  announce(message);
  setTimeout(() => useToasts.setState((s) => ({ list: s.list.filter((x) => x.id !== id) })), opts.ms ?? (opts.error ? 7000 : 4500));
}

export function Toasts() {
  const list = useToasts((s) => s.list);
  return (
    <div className="toasts" role="region" aria-label="Notifications">
      {list.map((x) => (
        <div key={x.id} className={`toast ${x.error ? 'err' : ''}`} role={x.error ? 'alert' : 'status'}>
          <div className="grow">{x.message}</div>
          {x.action && <button type="button" onClick={() => { x.action!.onClick(); useToasts.setState((s) => ({ list: s.list.filter((y) => y.id !== x.id) })); }}>{x.action.label}</button>}
        </div>
      ))}
    </div>
  );
}
