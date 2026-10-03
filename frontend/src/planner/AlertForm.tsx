// The alert form: compose a message for everyone inside a circle, see how many phones it will reach, send it.
// Used on the Alerts page and in the dialog opened from a map area or a report.

import { useEffect, useMemo, useRef, useState } from 'react';
import L from 'leaflet';
import { Banner, Icon, toast } from '../components/ui';
import { SearchBox } from '../components/chrome';
import { AddressLine } from '../lib/geo';
import { createAlert, errorText, getReach } from '../lib/api';
import { useDebounced } from '../lib/hooks';
import { tIn, useLang, useT } from '../lib/i18n';
import { useApp } from '../lib/store';
import type { LatLon, Place, SuggestedAction } from '../lib/types';
import { Circle, MapView, Marker, MapClick, useMap, KRAKOW } from '../map/MapView';
import { requireOnline, usePlanner } from './store';

const PIN_LITE = L.divIcon({ className: 'pin-lite', html: '<span></span>', iconSize: [18, 18], iconAnchor: [9, 9] });

type Template = 'heat' | 'night' | 'custom';
const TEMPLATES: Template[] = ['heat', 'night', 'custom'];

interface Props {
  cell?: Place | null;
  action?: SuggestedAction | null;
  point?: LatLon | null;
  onSent?: () => void;
}

/** Keeps the whole circle in view as its size changes. */
function FitCircle({ center, radius }: { center: LatLon; radius: number }) {
  const map = useMap();
  useEffect(() => {
    if (!map) return;
    const id = setTimeout(() => { map.invalidateSize(); map.fitBounds(L.latLng(center).toBounds(radius * 2).pad(0.3), { animate: false }); }, 50);
    return () => clearTimeout(id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, radius]);
  return null;
}

export function AlertForm({ cell, action, point, onSent }: Props) {
  const t = useT();
  const lang = useLang();
  const event = useApp((s) => s.event);
  const dataChanged = usePlanner((s) => s.dataChanged);

  const start: LatLon = cell ? [cell.latitude, cell.longitude] : point || KRAKOW;
  // Start from the template that fits: the action's layer, else the weaker layer of the area, else the planning event.
  const initialTemplate: Template = action?.layer === 'Heat' ? 'heat' : action?.layer === 'Safety' ? 'night'
    : cell ? (100 - cell.heat.score < cell.safety.score ? 'heat' : 'night')
      : event === 'heat' ? 'heat' : event === 'night' ? 'night' : 'custom';

  const [template, setTemplate] = useState<Template>(initialTemplate);
  const [center, setCenter] = useState<LatLon>(start);
  const [radius, setRadius] = useState(800);
  const [severity, setSeverity] = useState<'Info' | 'Warning' | 'Critical'>('Warning');
  const [layer, setLayer] = useState('');
  const [duration, setDuration] = useState(180);
  const [title, setTitle] = useState('');
  const [message, setMessage] = useState('');
  const [pl, setPl] = useState('');
  const [uk, setUk] = useState('');
  const [armed, setArmed] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reach, setReach] = useState('');
  const armTimer = useRef<ReturnType<typeof setTimeout>>();

  const applyTemplate = (id: Template) => {
    setTemplate(id);
    if (id === 'custom') return;
    const key = id === 'heat' ? 'al.tpl.heat' : 'al.tpl.night';
    setMessage(tIn('en', `${key}.body`)); setPl(tIn('pl', `${key}.body`)); setUk(tIn('uk', `${key}.body`));
    setSeverity(id === 'heat' ? 'Warning' : 'Info');
    setLayer(id === 'heat' ? 'Heat' : 'Safety');
    // The title is written for planners (English or Polish).
    setTitle(tIn(lang === 'pl' ? 'pl' : 'en', `${key}.title`));
  };
  useEffect(() => { applyTemplate(initialTemplate); }, []);

  // Any edit disarms the two-step send.
  useEffect(() => { setArmed(false); }, [title, message, pl, uk, severity, layer, duration, radius, center[0], center[1]]);

  const debouncedCenter = useDebounced(center, 400);
  const debouncedRadius = useDebounced(radius, 400);
  useEffect(() => {
    if (!navigator.onLine) { setReach(t('al.reachOffline')); return; }
    let live = true;
    getReach(debouncedCenter[0], debouncedCenter[1], debouncedRadius)
      .then((r) => { if (live) setReach(t('al.reach', { n: r.devicesInArea, total: r.devicesActive, min: r.windowMinutes })); })
      .catch(() => { if (live) setReach(''); });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedCenter[0], debouncedCenter[1], debouncedRadius]);

  const radiusText = radius >= 1000 ? `${(radius / 1000).toFixed(1)} km` : `${radius} m`;
  const previewKind = severity === 'Critical' ? 'critical' : severity === 'Info' ? 'info' : '';
  const previewText = (lang === 'pl' && pl) || (lang === 'uk' && uk) || message || t('al.messagePlaceholder');
  const mapCenter = useMemo(() => start, []);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!requireOnline()) return;
    // Two-step send: the first press arms the button, the second sends. A mistaken tap should not alert a neighbourhood.
    if (!armed) {
      setArmed(true);
      clearTimeout(armTimer.current);
      armTimer.current = setTimeout(() => setArmed(false), 5000);
      return;
    }
    setSending(true);
    try {
      const translations: Record<string, string> = {};
      if (pl.trim()) translations.pl = pl.trim();
      if (uk.trim()) translations.uk = uk.trim();
      const a = await createAlert({
        layer: layer || null, severity, title, message, translations,
        latitude: center[0], longitude: center[1], radiusMeters: radius, durationMinutes: duration, cellId: cell?.cellId ?? null
      });
      toast(t('al.sent', { n: a.devicesInArea ?? 0 }));
      dataChanged();
      onSent?.();
    } catch (err) {
      setError(errorText(err, t));
      setArmed(false);
    } finally { setSending(false); }
  };

  return (
    <form className="stack" noValidate onSubmit={submit}>
      <label className="field">{t('al.template')}
        <select value={template} onChange={(e) => applyTemplate(e.target.value as Template)}>
          {TEMPLATES.map((id) => <option key={id} value={id}>{t(`al.tpl.${id}`)}</option>)}
        </select>
      </label>
      <div className="row wrap">
        <label className="field grow">{t('al.severity')}
          <select value={severity} onChange={(e) => setSeverity(e.target.value as typeof severity)}>{(['Info', 'Warning', 'Critical'] as const).map((s) => <option key={s} value={s}>{t(`sev.${s}`)}</option>)}</select>
        </label>
        <label className="field grow">{t('al.layer')}
          <select value={layer} onChange={(e) => setLayer(e.target.value)}>
            <option value="">{t('al.layerAny')}</option><option value="Heat">{t('mode.heat')}</option><option value="Safety">{t('mode.safety')}</option>
          </select>
        </label>
        <label className="field grow">{t('al.duration')}
          <select value={duration} onChange={(e) => setDuration(Number(e.target.value))}>
            {([[30, '30 min'], [60, '1 h'], [180, '3 h'], [360, '6 h'], [720, '12 h'], [1440, '24 h']] as const).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </label>
      </div>
      <label className="field">{t('al.title')}<input type="text" maxLength={80} required value={title} onChange={(e) => setTitle(e.target.value)} /></label>
      <label className="field">{t('al.message')}<textarea rows={3} maxLength={500} required value={message} onChange={(e) => setMessage(e.target.value)} /></label>
      <details>
        <summary className="small">{t('al.translations')}</summary>
        <div className="stack">
          <label className="field">Polski<textarea rows={2} maxLength={500} placeholder={t('al.optional')} value={pl} onChange={(e) => setPl(e.target.value)} /></label>
          <label className="field">Українська<textarea rows={2} maxLength={500} placeholder={t('al.optional')} value={uk} onChange={(e) => setUk(e.target.value)} /></label>
          <p className="tiny muted">{t('al.translationsHelp')}</p>
        </div>
      </details>
      <div className="field">
        <span>{t('al.area')}</span><span className="hint">{t('al.areaHelp')}</span>
        <SearchBox label={t('al.searchLabel')} placeholder={t('al.searchPlaceholder')} filters={false} near={() => center} onPick={(r) => setCenter([r.lat, r.lon])} />
        <MapView className="mini-map" label={t('al.mapLabel')} center={mapCenter} zoom={14} zoomControl={false}>
          <Circle center={center} radius={radius} options={{ color: '#d03b3b', weight: 2, fillOpacity: 0.12 }} />
          <Marker position={center} icon={PIN_LITE} draggable onDragEnd={setCenter} />
          <MapClick onClick={setCenter} />
          <FitCircle center={mapCenter} radius={radius} />
        </MapView>
        <AddressLine lat={center[0]} lon={center[1]} />
        <div className="row small"><Icon name="target" size="sm" /><span>{t('al.radius')}: </span><b>{radiusText}</b><span className="muted"> · {t('al.radiusHelp')}</span></div>
        <input type="range" min={100} max={3000} step={100} value={radius} aria-label={t('al.radius')} onChange={(e) => setRadius(Number(e.target.value))} />
        <p className="small" aria-live="polite">{reach}</p>
      </div>
      <div className="preview">
        <p className="sect-title">{t('al.preview')}</p>
        <Banner kind={previewKind as any} icon={layer === 'Heat' ? 'thermo' : layer === 'Safety' ? 'moon' : 'bell'}>
          <b>{title || t('al.titlePlaceholder')}</b> <span className="small">{previewText}</span>
        </Banner>
      </div>
      {error && <p className="err" role="alert">{error}</p>}
      <div className="row wrap end">
        <button className={`btn primary ${armed ? 'danger' : ''}`} type="submit" disabled={sending}><Icon name="send" size="sm" />{armed ? t('al.confirm') : t('al.send')}</button>
      </div>
    </form>
  );
}
