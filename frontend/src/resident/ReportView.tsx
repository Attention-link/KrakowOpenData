// "Report a concern": choose what you noticed, place the pin (drag it, tap the map or type an address), add a note, send.
// Only the report types of the chosen view are offered (Heat: heat reports, Night safety: night reports).
// Offline, the report is queued on the device and sent automatically when the connection is back.

import { useEffect, useMemo, useState } from 'react';
import { Banner, Icon, toast } from '../components/ui';
import { SearchBox } from '../components/chrome';
import { AddressLine } from '../lib/geo';
import { NetworkError, errorText, postReport } from '../lib/api';
import { useT } from '../lib/i18n';
import { cellBounds, cellOf } from '../lib/model';
import { outboxAdd } from '../lib/outbox';
import { isOffline, useApp, useOffline } from '../lib/store';
import type { LatLon, ReportType } from '../lib/types';
import { Marker, Rect, pinIcon, useMap } from '../map/MapView';
import { useResident } from './context';

const FALLBACK_TYPES = [
  { type: 'LightOut', layer: 'Safety' }, { type: 'UnsafeAtNight', layer: 'Safety' }, { type: 'PathHazard', layer: 'Safety' },
  { type: 'WaterNotWorking', layer: 'Heat' }, { type: 'NoShade', layer: 'Heat' }, { type: 'HeatSpot', layer: 'Heat' }
] as Pick<ReportType, 'type' | 'layer'>[];

const TYPE_ICON: Record<string, string> = { LightOut: 'lamp', UnsafeAtNight: 'moon', PathHazard: 'alert', WaterNotWorking: 'water', NoShade: 'sun', HeatSpot: 'thermo' };

type Done = { queued: boolean; supporters?: number };

export function ReportView() {
  const t = useT();
  const ctx = useResident();
  const map = useMap();
  const offline = useOffline();
  const me = useApp((s) => s.me);
  const deviceId = useApp((s) => s.deviceId);
  const { mode, grid } = ctx;

  const [step, setStep] = useState(1);
  const [type, setType] = useState<string | null>(null);
  const [note, setNote] = useState('');
  const [latlng, setLatlng] = useState<LatLon>(() => ctx.selected ? [ctx.selected.lat, ctx.selected.lon] : [ctx.map.getCenter().lat, ctx.map.getCenter().lng]);
  const [dragging, setDragging] = useState<LatLon | null>(null);
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState<Done | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Tapping the map moves the pin.
  useEffect(() => {
    if (done) { ctx.registerMapClick(null); return; }
    ctx.registerMapClick((p) => { setLatlng(p); return true; });
    return () => ctx.registerMapClick(null);
  }, [done]);

  // Only the types of the chosen view (Both offers everything).
  const all = ctx.reportTypes?.length ? ctx.reportTypes : FALLBACK_TYPES;
  const layer = mode === 'heat' ? 'Heat' : mode === 'safety' ? 'Safety' : null;
  const types = layer ? all.filter((x) => x.layer === layer) : all;
  useEffect(() => { if (type && !types.some((x) => x.type === type)) setType(null); }, [mode]);

  // The outlined square is what the report counts for.
  const square = useMemo(() => {
    if (!grid) return null;
    const p = dragging ?? latlng;
    const c = cellOf(grid.grid, p[0], p[1]);
    return cellBounds(grid.grid, c.row, c.col);
  }, [grid, latlng, dragging]);

  const moveTo = (ll: LatLon) => { map?.panTo(ll); setLatlng(ll); };

  const send = async () => {
    setError(null);
    setSending(true);
    const payload = { type, latitude: latlng[0], longitude: latlng[1], note: note.trim() || null, deviceId };
    try {
      if (isOffline()) throw new NetworkError('offline');
      const created = await postReport(payload);
      setDone({ queued: false, supporters: created.supporters });
      ctx.refreshGrid();
    } catch (e) {
      if (e instanceof NetworkError) { await outboxAdd({ kind: 'report', body: payload }); setDone({ queued: true }); }
      else setError(errorText(e, t));
    } finally { setSending(false); }
  };

  const pin = !done && <Marker position={latlng} icon={pinIcon('flag')} draggable z={1000} title={t('report.pin')}
    onDrag={setDragging} onDragEnd={(p) => { setDragging(null); setLatlng(p); }} />;
  const outline = square && !done && <Rect bounds={square} options={{ color: '#17171a', weight: 2, dashArray: '4 3', fill: false, interactive: false }} />;

  if (done) {
    return (
      <div className="stack done-pane">
        <div className={`done-ico ${done.queued ? 'queued' : ''}`}><Icon name={done.queued ? 'clock' : 'check'} size="lg" /></div>
        <h2>{done.queued ? t('report.queued') : t('report.thanks')}</h2>
        <p className="muted">{done.queued ? t('report.queuedHelp') : (done.supporters ?? 1) > 1 ? t('report.merged', { n: done.supporters! }) : t('report.single')}</p>
        <div className="row wrap center">
          <button className="btn primary" type="button" onClick={() => ctx.selectPoint(latlng[0], latlng[1])}>{done.queued ? t('common.done') : t('report.seeScore')}</button>
          <button className="btn" type="button" onClick={() => { setDone(null); setStep(1); setType(null); setNote(''); }}>{t('report.another')}</button>
          <button className="btn quiet" type="button" onClick={() => ctx.showView('home')}><Icon name="left" size="sm" />{t('nav.backMenu')}</button>
        </div>
      </div>
    );
  }

  return (
    <div className="stack">
      {pin}{outline}
      <div className="steps" role="img" aria-label={t('report.step', { n: step, total: 2 })}><i className="on" /><i className={step === 2 ? 'on' : ''} /></div>

      {step === 1 ? (
        <>
          <p>{t(`report.intro.${mode}`)}</p>
          <div className="type-grid" role="group" aria-label={t('report.what')}>
            {types.map((ty) => (
              <button key={ty.type} className="type-card" type="button" aria-pressed={type === ty.type} onClick={() => setType(ty.type)}>
                <span className={`ico ${ty.layer.toLowerCase()}`}><Icon name={TYPE_ICON[ty.type] || 'flag'} /></span>
                <b>{t(`rtype.${ty.type}`)}</b>
                <span className="tiny muted">{t(ty.layer === 'Heat' ? 'mode.heat' : 'mode.safety')}</span>
              </button>
            ))}
          </div>
          <Banner kind="danger" icon="alert" small><span>{t('report.emergency')}</span></Banner>
          <div className="row"><div className="grow" /><button className="btn primary" type="button" disabled={!type} onClick={() => setStep(2)}>{t('common.next')}<Icon name="right" size="sm" /></button></div>
        </>
      ) : (
        <>
          <div className="row">
            <span className="chip"><Icon name={TYPE_ICON[type!] || 'flag'} size="sm" />{t(`rtype.${type}`)}</span>
            <button className="btn sm quiet" type="button" onClick={() => setStep(1)}>{t('report.change')}</button>
          </div>
          <div className="card flat stack tight">
            <b>{t('report.where')}</b>
            <AddressLine lat={latlng[0]} lon={latlng[1]} />
            <p className="small muted"><Icon name="target" size="sm" /> {t('cov.report')}</p>
            <p className="small">{t('report.drag')}</p>
            <SearchBox label={t('report.searchLabel')} placeholder={t('report.searchPlaceholder')} filters={false} near={() => latlng} onPick={(r) => moveTo([r.lat, r.lon])} />
            <div className="row wrap">
              {ctx.selected && <button className="btn sm" type="button" onClick={() => moveTo([ctx.selected!.lat, ctx.selected!.lon])}>{t('report.useSelected')}</button>}
              {me && <button className="btn sm" type="button" onClick={() => moveTo([me.lat, me.lon])}>{t('report.useMine')}</button>}
            </div>
          </div>
          <label className="field">{t('report.note')}
            <textarea maxLength={200} rows={3} placeholder={t('report.notePlaceholder')} value={note} onChange={(e) => setNote(e.target.value)} aria-describedby="note-count" />
            <span id="note-count" className="hint">{note.length}/200</span>
          </label>
          <p className="tiny muted"><Icon name="shield" size="sm" /> {t('report.privacy')}</p>
          {error && <p className="err" role="alert">{error}</p>}
          {offline && <Banner icon="offline" small><span>{t('report.offlineNote')}</span></Banner>}
          <div className="row">
            <button className="btn" type="button" onClick={() => setStep(1)}><Icon name="left" size="sm" />{t('common.back')}</button>
            <div className="grow" />
            <button className="btn primary" type="button" disabled={sending} onClick={send}><Icon name="send" size="sm" />{sending ? t('report.sending') : offline ? t('report.saveOffline') : t('report.send')}</button>
          </div>
        </>
      )}
    </div>
  );
}

