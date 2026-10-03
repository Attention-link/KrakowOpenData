// Page chrome: top bar, connection status, language menu, notification switch, "how it works", and the address / stop search box.

import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Icon, openDialog, toast } from './ui';
import { openMethod } from './explain';
import { LANGS, t as translateNow, useLang, useT } from '../lib/i18n';
import { setApp, useApp, useOffline } from '../lib/store';
import { DOCS_URL, geoSearch, ping, searchStops } from '../lib/api';
import { flushOutbox, useOutboxCount } from '../lib/outbox';
import { timeAgo } from '../lib/util';
import { useDebounced } from '../lib/hooks';
import type { Lang, LatLon, SearchHit } from '../lib/types';

// ── Connection monitoring (mounted once at the app root) ────────────────────
export function useConnectivity() {
  const apiOk = useApp((s) => s.apiOk);
  const online = useApp((s) => s.online);

  useEffect(() => {
    const up = () => setApp({ online: true });
    const down = () => setApp({ online: false });
    window.addEventListener('online', up);
    window.addEventListener('offline', down);
    return () => { window.removeEventListener('online', up); window.removeEventListener('offline', down); };
  }, []);

  // While the API does not answer, probe it every 20 s; when it answers again, send queued reports.
  useEffect(() => {
    if (apiOk && online) { void flushOutbox(); return; }
    const check = async () => { try { if (await ping()) setApp({ apiOk: true }); } catch { /* still offline */ } };
    void check();
    const id = setInterval(check, 20000);
    return () => clearInterval(id);
  }, [apiOk, online]);
}

// ── Status pill ──────────────────────────────────────────────────────────────
export function StatusPill({ savedAt }: { savedAt?: number | null }) {
  const t = useT();
  const lang = useLang();
  const off = useOffline();
  const pending = useOutboxCount();
  return (
    <span className={`status-pill ${off ? 'off' : ''}`} role="status" title={off ? t('net.offlineHint') : t('net.onlineHint')}>
      <Icon name={off ? 'offline' : 'online'} size="sm" />
      <span className="hide-sm">{off ? t('net.offline') : t('net.online')}</span>
      {off && savedAt ? <span className="tiny hide-sm">· {timeAgo(savedAt, t, lang)}</span> : null}
      {pending > 0 && <span className="chip warn">{t('outbox.pending', { n: pending })}</span>}
    </span>
  );
}

export function LangSelect() {
  const t = useT();
  const lang = useLang();
  return (
    <select className="lang-select" aria-label={t('lang.label')} value={lang} onChange={(e) => setApp({ lang: e.target.value as Lang })}>
      {LANGS.map(([code, name]) => <option key={code} value={code}>{name}</option>)}
    </select>
  );
}

export function Topbar({ subtitle, right, savedAt }: { subtitle?: string; right?: ReactNode; savedAt?: number | null }) {
  const t = useT();
  return (
    <header className="topbar">
      <a className="brand" href="#/" aria-label={t('app.title')}>
        <img src="icon.svg" alt="" />
        <div className="truncate"><b>{t('app.title')}</b><span>{subtitle || t('app.subtitle')}</span></div>
      </a>
      <div className="spacer" />
      <StatusPill savedAt={savedAt} />
      {right}
      <LangSelect />
    </header>
  );
}

/** A switch for device notifications about alerts; asks the browser for permission when turned on. */
export function NotificationToggle() {
  const t = useT();
  const notify = useApp((s) => s.notify);
  const click = async () => {
    if (notify) { setApp({ notify: false }); return; }
    if (!('Notification' in window)) { toast(t('notify.unsupported'), { error: true }); return; }
    const result = await Notification.requestPermission();
    if (result === 'granted') { setApp({ notify: true }); toast(t('notify.granted')); }
    else toast(t('notify.denied'), { error: true });
  };
  return <button className="btn sm" type="button" aria-pressed={notify} onClick={click}><Icon name="bell" size="sm" />{notify ? t('notify.on') : t('notify.off')}</button>;
}

/** Applies the chosen theme and the document language. */
export function useDocumentSettings() {
  const theme = useApp((s) => s.theme);
  const lang = useLang();
  const t = useT();
  useEffect(() => {
    if (theme === 'light' || theme === 'dark') document.documentElement.dataset.theme = theme;
    else delete document.documentElement.dataset.theme;
  }, [theme]);
  useEffect(() => {
    document.documentElement.lang = lang;
    document.title = `${t('app.title')} · Kraków`;
    const skip = document.querySelector('[data-i18n="skip"]');
    if (skip) skip.textContent = t('skip');
  }, [lang]);
}

// ── Explanation of the scores (resident-facing) ──────────────────────────────
export function openHowItWorks() {
  openDialog((close) => {
    const tt = (k: string) => translateNow(k);
    return {
      title: tt('how.title'),
      body: (
        <div className="stack">
          <p>{tt('how.intro')}</p>
          <div><h3><span className="chip safety"><Icon name="moon" size="sm" />{tt('mode.safety')}</span></h3><p className="small">{tt('how.safety')}</p></div>
          <div><h3><span className="chip heat"><Icon name="sun" size="sm" />{tt('mode.heat')}</span></h3><p className="small">{tt('how.heat')}</p></div>
          <p className="small">{tt('how.reports')}</p>
          <p className="banner info small"><Icon name="info" /><span>{tt('how.notCrime')}</span></p>
        </div>
      ),
      footer: (
        <>
          <button className="btn" type="button" onClick={() => { close('x'); openMethod(); }}><Icon name="list" size="sm" />{tt('explain.fullMethod')}</button>
          <a className="btn" href={DOCS_URL} target="_blank" rel="noopener noreferrer"><Icon name="external" size="sm" />{tt('about.api')}</a>
          <button className="btn primary" type="button" onClick={() => close('ok')}>{tt('common.done')}</button>
        </>
      )
    };
  });
}


// ── Address / stop search box with autocomplete ─────────────────────────────
interface SearchBoxProps {
  label?: string;
  placeholder: string;
  near?: () => LatLon | null;
  onPick: (hit: SearchHit) => void;
  filters?: boolean;
  initial?: string;
}

type Filter = 'all' | 'address' | 'stop';

/** Addresses come from OpenStreetMap through the API; stops from the timetable data. Needs a connection. */
export function SearchBox({ label, placeholder, near, onPick, filters = true, initial = '' }: SearchBoxProps) {
  const t = useT();
  const id = useId();
  const off = useOffline();
  const [text, setText] = useState(initial);
  const [results, setResults] = useState<SearchHit[]>([]);
  const [filter, setFilter] = useState<Filter>('all');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const [status, setStatus] = useState('');
  const picked = useRef<string | null>(initial || null);
  const debounced = useDebounced(text, 300);
  const nearRef = useRef(near);
  nearRef.current = near;

  useEffect(() => { setText(initial); picked.current = initial || null; }, [initial]);

  useEffect(() => {
    const q = debounced.trim();
    if (picked.current === q) return;           // do not search again for the text of a result just picked
    let live = true;
    if (q.length < 2) { setResults([]); setStatus(''); return; }
    setStatus(t('search.searching'));
    void Promise.allSettled([geoSearch(q, nearRef.current?.()), searchStops(q)]).then(([addr, stops]) => {
      if (!live) return;
      const all: SearchHit[] = [];
      if (addr.status === 'fulfilled') all.push(...addr.value.map((a): SearchHit => ({ kind: 'address', label: a.label, lat: a.latitude, lon: a.longitude, detail: a.postcode || null })));
      if (stops.status === 'fulfilled') all.push(...(stops.value.items || []).map((s): SearchHit => ({ kind: 'stop', label: s.name + (s.code ? ` ${s.code}` : ''), lat: s.latitude, lon: s.longitude, detail: t('search.stopDetail') })));
      // The same stop appears once per timetable feed: keep one.
      const seen = new Set<string>();
      const unique = all.filter((r) => { const k = `${r.kind}|${r.label}|${r.lat.toFixed(3)}|${r.lon.toFixed(3)}`; return seen.has(k) ? false : (seen.add(k), true); });
      setResults(unique);
      setOpen(true);
      setActive(-1);
      setStatus(addr.status === 'rejected' && stops.status === 'rejected' ? t('search.error') : unique.length ? '' : t('search.none'));
    });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debounced]);

  const shown = results.filter((r) => filter === 'all' || r.kind === filter);

  const pick = (r: SearchHit) => {
    picked.current = r.label;
    setText(r.label);
    setResults([]);
    setOpen(false);
    setStatus('');
    onPick(r);
  };

  const key = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown' && shown.length) { e.preventDefault(); setOpen(true); setActive((a) => (a + 1) % shown.length); }
    else if (e.key === 'ArrowUp' && shown.length) { e.preventDefault(); setActive((a) => (a - 1 + shown.length) % shown.length); }
    else if (e.key === 'Enter') { const r = shown[active] ?? shown[0]; if (r) { e.preventDefault(); pick(r); } }
    else if (e.key === 'Escape') setOpen(false);
  };

  return (
    <div className="field sb">
      {label && <label htmlFor={id}>{label}</label>}
      <div className="sb-wrap">
        <input id={id} type="search" autoComplete="off" placeholder={placeholder} role="combobox" aria-expanded={open && shown.length > 0}
          aria-controls={`${id}-list`} aria-autocomplete="list" enterKeyHint="search" spellCheck={false} disabled={off} value={text}
          aria-activedescendant={active >= 0 ? `${id}-o${active}` : undefined}
          onChange={(e) => { picked.current = null; setText(e.target.value); }}
          onFocus={() => { if (results.length) setOpen(true); }}
          onBlur={() => setTimeout(() => setOpen(false), 120)}
          onKeyDown={key} />
        <ul className="sb-list" id={`${id}-list`} role="listbox" hidden={!open || shown.length === 0} aria-label={t('search.results')}>
          {shown.map((r, i) => (
            <li key={`${r.kind}${r.label}${i}`} id={`${id}-o${i}`} role="option" aria-selected={i === active} className="sb-item"
              onMouseDown={(e) => { e.preventDefault(); pick(r); }}>
              <Icon name={r.kind === 'stop' ? 'bus' : 'pin'} size="sm" />
              <span className="grow"><span className="sb-title">{r.label}</span>{r.detail && <span className="tiny muted block">{r.detail}</span>}</span>
              <span className="chip">{t(r.kind === 'stop' ? 'search.stop' : 'search.address')}</span>
            </li>
          ))}
        </ul>
      </div>
      {filters && (
        <div className="row wrap sb-filters" role="group" aria-label={t('search.filter')}>
          <span className="tiny muted">{t('search.show')}</span>
          {([['all', 'search.all'], ['address', 'search.addresses'], ['stop', 'search.stops']] as [Filter, string][]).map(([k, labelKey]) => (
            <button key={k} type="button" className={`chip-btn${filter === k ? ' on' : ''}`} aria-pressed={filter === k} onClick={() => setFilter(k)}>{t(labelKey)}</button>
          ))}
        </div>
      )}
      <p className="tiny muted" aria-live="polite">{status}</p>
      {off && <p className="tiny muted">{t('search.offline')}</p>}
    </div>
  );
}
