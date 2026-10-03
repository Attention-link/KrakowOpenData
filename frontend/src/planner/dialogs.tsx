// Planner dialogs: the alert composer, the "contact an agency" form with a generated brief, and the resolve-a-report dialog.

import { useState } from 'react';
import { Icon, openDialog, toast } from '../components/ui';
import { createDispatch, errorText, resolveReport } from '../lib/api';
import { t, tIn } from '../lib/i18n';
import { bandOf } from '../lib/model';
import { copyText, formatDistance, geoLink } from '../lib/util';
import type { Agency, Lang, Place, Report, SuggestedAction, LatLon } from '../lib/types';
import { AlertForm } from './AlertForm';
import { loadAgencies, requireOnline, usePlanner } from './store';

// ── Agency choice per suggested action ───────────────────────────────────────
const AGENCY_FOR_ACTION: Record<string, string> = {
  ADD_WATER_POINT: 'crisis', ADD_SHADE: 'zzm', OPEN_COOL_SPACE: 'crisis', ADD_TOILET: 'portal', REVIEW_STOPS: 'ztp',
  FIX_LIGHTING: 'zdmk', REVIEW_NIGHT_SERVICE: 'ztp', ADD_NIGHT_PRESENCE: 'straz', ADD_AED: 'portal', REVIEW_REPORTS: 'portal'
};
export const agencyForAction = (code: string) => AGENCY_FOR_ACTION[code] || 'portal';

// ── Brief for an agency ──────────────────────────────────────────────────────
export function makeBrief(lang: Lang, { cell, action }: { cell: Place; action?: SuggestedAction | null }): string {
  const L = (k: string, p?: Record<string, string | number>) => tIn(lang, k, p);
  const meta = usePlanner.getState().grid?.grid;
  const band = (s: number, kind: 'good' | 'heat' = 'good') => L(kind === 'heat' ? `band.heat.${bandOf(s, meta, 'heat')}` : `band.${bandOf(s, meta)}`);
  const weak = [...cell.safety.factors, ...cell.heat.factors].filter((f) => f.score < 35)
    .map((f) => `${L(`factor.${f.key}`)} (${f.unit === 'm' ? (f.value === null ? L('factor.none') : formatDistance(f.value)) : `${Math.round(f.value ?? 0)} ${L('factor.lamps')}`})`);
  const reports = cell.reports.filter((r) => r.status === 'Open');
  const c = usePlanner.getState().summary?.data.conditions || null;
  const lines = [
    L('brief.location', { lat: cell.latitude.toFixed(5), lon: cell.longitude.toFixed(5), cell: cell.cellId }),
    geoLink(cell.latitude, cell.longitude),
    c ? L('brief.situation', { heat: L(`heat.${c.heat.pressure}`), dark: c.isDark ? L('brief.dark') : L('brief.daylight') }) : null,
    L('brief.scores', { safety: Math.round(cell.safety.score), sb: band(cell.safety.score), heat: Math.round(cell.heat.score), hb: band(cell.heat.score, 'heat'), priority: Math.round(cell.priority) }),
    weak.length ? L('brief.weak', { list: weak.join('; ') }) : null,
    reports.length ? L('brief.reports', { n: reports.length, types: [...new Set(reports.map((r) => L(`rtype.${r.type}`)))].join('; ') }) : null,
    action ? L('brief.request', { text: L(`action.${action.code}`) }) : null,
    '',
    L('brief.source')
  ];
  return lines.filter((x) => x !== null).join('\n');
}

// ── Contact an agency ────────────────────────────────────────────────────────
function DispatchForm({ agencies, cell, action, agencyId, close, onSent }: { agencies: Agency[]; cell?: Place | null; action?: SuggestedAction | null; agencyId?: string; close: () => void; onSent: () => void }) {
  // Agencies write and read Polish; the planner can switch to English.
  const [lang, setLang] = useState<Lang>('pl');
  const [agency, setAgency] = useState(agencyId || (action ? agencyForAction(action.code) : agencies[0]?.id));
  const subjectFor = (l: Lang) => (cell ? tIn(l, 'brief.subject', { cell: cell.cellId, action: action ? tIn(l, `action.${action.code}`) : tIn(l, 'brief.generic') }) : '');
  const [subject, setSubject] = useState(subjectFor('pl'));
  const [body, setBody] = useState(cell ? makeBrief('pl', { cell, action }) : '');
  const [busy, setBusy] = useState(false);
  const a = agencies.find((x) => x.id === agency);

  const changeLang = (l: Lang) => {
    setLang(l);
    setSubject(subjectFor(l));
    if (cell) setBody(makeBrief(l, { cell, action }));
  };
  const send = async () => {
    setBusy(true);
    try {
      const d = await createDispatch({ agencyId: agency, subject, body, latitude: cell?.latitude ?? null, longitude: cell?.longitude ?? null, cellId: cell?.cellId ?? null });
      toast(t('ag.recorded', { ref: d.reference }), { ms: 9000 });
      onSent();
      close();
    } catch (e) { toast(errorText(e, t), { error: true }); setBusy(false); }
  };

  return (
    <div className="stack">
      <p className="banner info small"><Icon name="info" size="sm" /><span>{t('ag.simulated')}</span></p>
      <label className="field">{t('ag.agency')}
        <select value={agency} onChange={(e) => setAgency(e.target.value)}>{agencies.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select>
      </label>
      {a && (
        <div className="card flat small">
          <b>{a.name}</b>
          <div className="muted">{a.responsibility}</div>
          <div className="row wrap">
            {a.phone && <a className="btn sm" href={`tel:${a.phone.replace(/\s/g, '')}`}><Icon name="phone" size="sm" />{a.phone}</a>}
            {a.phone && !a.contactVerified && <span className="chip warn">{t('ag.verify')}</span>}
            {a.url && <a className="btn sm" href={a.url} target="_blank" rel="noopener noreferrer"><Icon name="external" size="sm" />{t('ag.website')}</a>}
          </div>
        </div>
      )}
      <div className="row wrap">
        <label className="field grow">{t('ag.subject')}<input type="text" maxLength={120} value={subject} onChange={(e) => setSubject(e.target.value)} /></label>
        <label className="field">{t('ag.language')}
          <select value={lang} onChange={(e) => changeLang(e.target.value as Lang)}><option value="pl">Polski</option><option value="en">English</option></select>
        </label>
      </div>
      <label className="field">{t('ag.message')}<textarea rows={11} maxLength={2000} value={body} onChange={(e) => setBody(e.target.value)} /></label>
      <div className="row wrap end">
        <button className="btn" type="button" onClick={async () => { if (await copyText(`${subject}\n\n${body}`)) toast(t('ag.copied')); }}><Icon name="copy" size="sm" />{t('ag.copy')}</button>
        <button className="btn" type="button" onClick={close}>{t('common.cancel')}</button>
        <button className="btn primary" type="button" disabled={busy} onClick={send}><Icon name="send" size="sm" />{t('ag.record')}</button>
      </div>
    </div>
  );
}

export async function openDispatchComposer({ cell, action, agencyId }: { cell?: Place | null; action?: SuggestedAction | null; agencyId?: string } = {}) {
  if (!requireOnline()) return;
  let agencies: Agency[];
  try { agencies = (await loadAgencies()).data; } catch (e) { toast(errorText(e, t), { error: true }); return; }
  openDialog((close) => ({
    title: t('ag.title'),
    body: <DispatchForm agencies={agencies} cell={cell} action={action} agencyId={agencyId} close={() => close('c')} onSent={() => usePlanner.getState().dataChanged()} />
  }));
}

// ── Alert composer in a dialog (the Alerts page embeds the same form) ────────
export function openAlertDialog({ cell, action, point }: { cell?: Place | null; action?: SuggestedAction | null; point?: LatLon | null } = {}) {
  if (!requireOnline()) return;
  openDialog((close) => ({
    title: t('al.new'),
    body: <AlertForm cell={cell} action={action} point={point} onSent={() => close('ok')} />
  }));
}

// ── Resolve a report ─────────────────────────────────────────────────────────
function ResolveForm({ r, close, onDone }: { r: Report; close: () => void; onDone?: () => void }) {
  const [note, setNote] = useState('');
  const go = async () => {
    try {
      await resolveReport(r.id, note.trim() || null);
      toast(t('rep.resolvedToast'));
      usePlanner.getState().dataChanged();
      onDone?.();
      close();
    } catch (e) { toast(errorText(e, t), { error: true }); }
  };
  return (
    <div className="stack">
      <p>{t(`rtype.${r.type}`)}</p>
      <label className="field">{t('rep.resolveNote')}<textarea maxLength={200} rows={3} placeholder={t('rep.resolvePlaceholder')} value={note} onChange={(e) => setNote(e.target.value)} /></label>
      <div className="row wrap end"><button className="btn" type="button" onClick={close}>{t('common.cancel')}</button><button className="btn primary" type="button" onClick={go}>{t('rep.resolve')}</button></div>
    </div>
  );
}

export function resolveDialog(r: Report, onDone?: () => void) {
  if (!requireOnline()) return;
  openDialog((close) => ({ title: t('rep.resolveTitle'), body: <ResolveForm r={r} close={() => close('c')} onDone={onDone} /> }));
}
