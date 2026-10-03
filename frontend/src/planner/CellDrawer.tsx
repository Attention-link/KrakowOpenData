// The drill-down drawer for one area: scores, why, reports, suggested actions, nearest assets. Every figure explains itself.

import { useEffect, useRef } from 'react';
import { Banner, Explainable, Icon, Skeleton, toast } from '../components/ui';
import { bandText, openFactorExplainer, openKpiExplainer, openScoreExplainer } from '../components/explain';
import { AddressLine } from '../lib/geo';
import { errorText, getCell, verifyReport } from '../lib/api';
import { useCachedQuery } from '../lib/hooks';
import { useLang, useT } from '../lib/i18n';
import { FACTOR_ICON, RELIEF, bandOf, kindOf } from '../lib/model';
import { useApp } from '../lib/store';
import { formatDistance, timeAgo } from '../lib/util';
import type { LayerKey, Report } from '../lib/types';
import { Gauge } from './charts';
import { openAlertDialog, openDispatchComposer, resolveDialog } from './dialogs';
import { goToMap, requireOnline, usePlanner } from './store';

export function StaleBanner({ savedAt }: { savedAt: number }) {
  const t = useT();
  const lang = useLang();
  return <Banner small icon="offline" role="status"><span>{t('pl.savedData', { when: timeAgo(savedAt, t, lang) })}</span></Banner>;
}

/** One report with the actions a planner needs. */
export function ReportItem({ r, onChange, children }: { r: Report; onChange?: () => void; children?: React.ReactNode }) {
  const t = useT();
  const lang = useLang();
  const dataChanged = usePlanner((s) => s.dataChanged);
  const demo = r.note?.startsWith('DEMO');
  const verify = async () => {
    if (!requireOnline()) return;
    try { await verifyReport(r.id); toast(t('rep.verifiedToast')); dataChanged(); onChange?.(); } catch (e) { toast(errorText(e, t), { error: true }); }
  };
  return (
    <li>
      <div className="row between wrap">
        <div className="grow">
          <b>{t(`rtype.${r.type}`)}</b> {demo && <span className="tag-demo">DEMO</span>}
          <div className="small muted">{`${t('report.supporters', { n: r.supporters })} · ${timeAgo(r.lastActivityAt, t, lang)} · ${r.status === 'Resolved' ? t('rep.resolved') : r.verifiedByPlanner ? t('report.verified') : t('rep.unverified')}`}</div>
          {r.note && !demo && <p className="small">{r.note}</p>}
          {r.resolutionNote && <p className="small muted">{t('rep.resolution')}: {r.resolutionNote}</p>}
        </div>
        {r.status === 'Open' && (
          <div className="row wrap">
            {!r.verifiedByPlanner && <button className="btn sm" type="button" onClick={verify}><Icon name="check" size="sm" />{t('rep.verify')}</button>}
            <button className="btn sm" type="button" onClick={() => resolveDialog(r, onChange)}>{t('rep.resolve')}</button>
          </div>
        )}
      </div>
      {children}
    </li>
  );
}

export function CellDrawer({ cellId }: { cellId: string }) {
  const t = useT();
  const event = useApp((s) => s.event);
  const grid = usePlanner((s) => s.grid);
  const openCell = usePlanner((s) => s.openCell);
  const version = usePlanner((s) => s.dataVersion);
  const q = useCachedQuery(`p:cell:${cellId}:${event}`, () => getCell(cellId, event, true), [version]);
  const gm = grid?.grid;
  const close = () => openCell(null);
  const headRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headRef.current?.focus({ preventScroll: true }); }, [cellId]);   // once per area, not on every re-render

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, []);

  const c = q.data;
  // The drawer follows the planning event: Heat shows heat only, Night safety night safety only, Both shows both.
  const layers: LayerKey[] = event === 'heat' ? ['heat'] : event === 'night' ? ['safety'] : ['safety', 'heat'];
  const wanted = new Set<string>(layers.map((k) => (k === 'heat' ? 'Heat' : 'Safety')));
  const title = c ? (c.label ? `${t('cell.near', { name: c.label })} · ${cellId}` : t('cell.title', { id: cellId })) : t('cell.title', { id: cellId });

  return (
    <>
      <div className="scrim" onClick={close} />
      <aside className="drawer" role="dialog" aria-modal="false" aria-label={title}>
        <div className="dr-head">
          <h2 className="grow" tabIndex={-1} ref={headRef}>{title}</h2>
          <button className="btn icon quiet" type="button" aria-label={t('common.close')} onClick={close}><Icon name="x" /></button>
        </div>
        <div className="dr-body">
          {!c && !q.error && <div className="stack"><Skeleton height={90} /><Skeleton height={200} /></div>}
          {!c && q.error != null && <Banner kind="danger" icon="alert"><span>{errorText(q.error, t)}</span></Banner>}
          {c && (
            <div className="stack">
              {q.stale && q.savedAt && <StaleBanner savedAt={q.savedAt} />}
              <div className="row between wrap">
                <div className="stack tight">
                  <AddressLine lat={c.latitude} lon={c.longitude} fallback={c.label ? t('cell.near', { name: c.label }) : null} />
                  <span className="small muted"><Icon name="target" size="sm" /> {t('cov.square', { r: formatDistance(gm?.cellSizeMeters || 250) })}</span>
                </div>
                <Explainable onOpen={() => openKpiExplainer('priority', { value: c.priority, extra: [
                  [t('cell.exposureLabel'), `${Math.round(c.exposure * 100)} / 100`], [t('mode.heat.score'), Math.round(c.heat.score)], [t('mode.safety.score'), Math.round(c.safety.score)], [t('mode.both.score'), Math.round(c.combined)]] })} hint={t('cell.priority')}>
                  <span className="chip accent">{t('cell.priority')} {Math.round(c.priority)}</span>
                </Explainable>
              </div>

              <div className="row wrap gauges">
                {layers.map((k) => (
                  <Explainable key={k} as="div" className="gauge-box" onOpen={() => openScoreExplainer({ layer: k, place: c, meta: gm })} hint={t(`mode.${k}.score`)}>
                    <Gauge score={c[k].score} meta={gm} label={t(`mode.${k}.score`)} kind={kindOf(k)} />
                    <div className="tiny muted">{t(`mode.${k}.score`)}</div>
                    <div className="tiny muted">{bandText(bandOf(c[k].score, gm, kindOf(k)), kindOf(k))}</div>
                  </Explainable>
                ))}
                {layers.length > 1 && (
                  <Explainable as="div" className="gauge-box" onOpen={() => openScoreExplainer({ layer: 'both', place: c, meta: gm })} hint={t('mode.both.score')}>
                    <Gauge score={c.combined} meta={gm} label={t('mode.both.score')} />
                    <div className="tiny muted">{t('mode.both.score')}</div>
                    <div className="tiny muted">{bandText(bandOf(c.combined, gm))}</div>
                  </Explainable>
                )}
              </div>

              <section>
                <h3>{t('cell.why')}</h3>
                {layers.map((k) => {
                  const l = c[k];
                  return (
                    <div key={k} className="why-layer">
                      <Explainable as="p" className="sect-title" onOpen={() => openScoreExplainer({ layer: k, place: c, meta: gm })} hint={t(`mode.${k}.score`)}>
                        {k === 'heat'
                          ? `${t('mode.heat.score')} · ${t('cell.base')} ${Math.round(l.baseScore)}${l.reportPenalty ? ` + ${l.reportPenalty} ${t('place.fromReports')}` : ''} = ${Math.round(l.score)} (${t('explain.dir.heat')})`
                          : `${t('mode.safety.score')} · ${t('cell.base')} ${Math.round(l.baseScore)}${l.reportPenalty ? ` − ${l.reportPenalty} ${t('place.fromReports')}` : ''} = ${Math.round(l.score)} (${t('explain.dir.safety')})`}
                      </Explainable>
                      <div className="tbl-wrap">
                        <table className="t">
                          <thead><tr>{[t('cell.factor'), t('cell.weight'), t('cell.value'), t('cell.score'), k === 'heat' ? t('explain.colHeat') : t('explain.colSafety')].map((x) => <th scope="col" key={x}>{x}</th>)}</tr></thead>
                          <tbody>
                            {l.factors.map((f) => {
                              const open = () => openFactorExplainer(f.key, { factor: f, layer: k, meta: gm });
                              return (
                                <tr key={f.key}>
                                  <td><Explainable onOpen={open} hint={t(`factor.${f.key}`)}><span className="row"><Icon name={FACTOR_ICON[f.key] || 'info'} size="sm" />{t(`factor.${f.key}`)}</span></Explainable></td>
                                  <td className="num"><Explainable onOpen={open} hint={t('cell.weight')}>{f.weight}</Explainable></td>
                                  <td className="num"><Explainable onOpen={open} hint={t('cell.value')}>{f.unit === 'm' ? (f.value === null ? t('factor.none', { r: formatDistance(gm?.searchRadiusMeters || 1500) }) : formatDistance(f.value)) : `${Math.round(f.value ?? 0)} ${t('factor.lamps')}`}</Explainable></td>
                                  <td><Explainable onOpen={open} hint={t('cell.score')}><span className="band" data-band={bandOf(f.score, gm)}>{Math.round(f.score)}</span></Explainable></td>
                                  <td className="num"><Explainable onOpen={open} hint={k === 'heat' ? t('explain.colHeat') : t('explain.colSafety')}>+{f.contribution}</Explainable></td>
                                </tr>
                              );
                            })}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  );
                })}
                <p className="tiny muted">{t('cell.exposure', { n: Math.round(c.exposure * 100) })}</p>
              </section>

              {c.reports.filter((r) => wanted.has(r.layer)).length > 0 && (
                <section>
                  <h3>{t('cell.reports', { n: c.reports.filter((r) => wanted.has(r.layer)).length })}</h3>
                  <ul className="list">{c.reports.filter((r) => wanted.has(r.layer)).map((r) => <ReportItem key={r.id} r={r} onChange={() => void q.reload()} />)}</ul>
                </section>
              )}

              <section>
                <h3>{t('cell.actions')}</h3>
                {c.actions.length ? (
                  <ul className="list">
                    {c.actions.map((a) => (
                      <li key={a.code + a.factorKey}>
                        <div className="row between wrap">
                          <div className="grow">
                            <b>{t(`action.${a.code}`)}</b>
                            <div className="small muted"><span className={`chip ${a.severity === 'high' ? 'danger' : 'warn'}`}>{t(`sev.${a.severity}`)}</span> {a.layer === 'Both' ? '' : t(`mode.${a.layer.toLowerCase()}`)}</div>
                          </div>
                          <div className="row wrap">
                            <button className="btn sm" type="button" onClick={() => openDispatchComposer({ cell: c, action: a })}><Icon name="send" size="sm" />{t('cell.contact')}</button>
                            <button className="btn sm" type="button" onClick={() => openAlertDialog({ cell: c, action: a })}><Icon name="bell" size="sm" />{t('cell.alert')}</button>
                          </div>
                        </div>
                      </li>
                    ))}
                  </ul>
                ) : <p className="small muted">{t('cell.noActions')}</p>}
              </section>

              {(() => {
                const keys = RELIEF[event === 'night' ? 'safety' : event];
                const shown = c.nearest.filter((n) => keys.includes(n.key));
                return shown.length > 0 && (
                  <section>
                    <h3>{t('cell.nearest')}</h3>
                    <div className="tbl-wrap"><table className="t"><tbody>
                      {shown.map((n) => (
                        <tr key={n.key}>
                          <td><span className="row"><Icon name={FACTOR_ICON[n.key] || 'pin'} size="sm" />{t(`factor.${n.key}`)}</span></td>
                          <td>{n.name || t(`kind.${n.kind}`)}</td>
                          <td className="num">{formatDistance(n.distanceMeters)} · {t('place.walkMin', { n: n.walkingMinutes })}</td>
                        </tr>
                      ))}
                    </tbody></table></div>
                  </section>
                );
              })()}
            </div>
          )}
        </div>
        {c && (
          <div className="dr-foot">
            <button className="btn" type="button" onClick={() => { close(); goToMap(c.latitude, c.longitude, cellId); }}><Icon name="map" size="sm" />{t('cell.showMap')}</button>
            <button className="btn" type="button" onClick={() => openAlertDialog({ cell: c })}><Icon name="bell" size="sm" />{t('cell.alertHere')}</button>
            <button className="btn primary" type="button" onClick={() => openDispatchComposer({ cell: c })}><Icon name="send" size="sm" />{t('cell.contactAgency')}</button>
          </div>
        )}
      </aside>
    </>
  );
}
