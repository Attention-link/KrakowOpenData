// The menu: find a place, use my location, check a walk, set my area for alerts, and how to read the colours.

import { Icon } from '../components/ui';
import { LegendBody, openMethod, openScoreExplainer } from '../components/explain';
import { NotificationToggle, SearchBox, openHowItWorks } from '../components/chrome';
import { AddressLine } from '../lib/geo';
import { DOCS_URL } from '../lib/api';
import { useT, useLang } from '../lib/i18n';
import { setApp, useApp } from '../lib/store';
import { timeAgo } from '../lib/util';
import type { Conditions } from '../lib/types';
import { useResident } from './context';

export function HomeView({ conditions }: { conditions: Conditions | null }) {
  const t = useT();
  const lang = useLang();
  const ctx = useResident();
  const me = useApp((s) => s.me);
  const { mode, map, grid } = ctx;
  const c = conditions;

  return (
    <div className="stack">
      {c && c.suggestedMode !== 'both' && mode !== c.suggestedMode && (
        <div className="banner info">
          <Icon name="info" />
          <div className="grow">
            {t(`home.suggest.${c.suggestedMode}`)}
            <div><button className="btn sm" type="button" onClick={() => setApp({ mode: c.suggestedMode })}>{t('home.switch', { mode: t(`mode.${c.suggestedMode}`) })}</button></div>
          </div>
        </div>
      )}

      <SearchBox label={t('home.searchLabel')} placeholder={t('home.searchPlaceholder')}
        near={() => { const m = map.getCenter(); return [m.lat, m.lng]; }}
        onPick={(r) => ctx.selectPoint(r.lat, r.lon, { fly: true, label: r.label })} />

      <div className="row wrap">
        <button className="btn primary" type="button" onClick={() => ctx.locateMe()}><Icon name="locate" />{t('home.useLocation')}</button>
        <button className="btn" type="button" onClick={() => ctx.showView('walk')}><Icon name="walk" />{t(`home.walk.${mode}`)}</button>
      </div>
      <p className="small muted">{t(`home.walkHelp.${mode}`)}</p>
      <p className="small muted">{t('home.tapHint')}</p>

      <div className="card flat">
        <h3>{t('home.alertsTitle')}</h3>
        {me ? (
          <div className="stack tight">
            <p className="small muted">
              <Icon name="bell" size="sm" /> {t('home.alertsOn')}
              {ctx.alertsLastChecked ? ` · ${t('alerts.checked', { when: timeAgo(ctx.alertsLastChecked, t, lang) })}` : ''}
            </p>
            <AddressLine lat={me.lat} lon={me.lon} />
            <div className="row wrap">
              <NotificationToggle />
              <button className="btn sm quiet" type="button" onClick={() => { setApp({ me: null }); ctx.refreshAlerts(); }}>{t('home.clearArea')}</button>
            </div>
          </div>
        ) : (
          <div className="stack tight">
            <p className="small muted">{t('home.alertsHelp')}</p>
            <div className="row wrap">
              <button className="btn sm" type="button" onClick={() => ctx.locateMe(true)}><Icon name="bell" size="sm" />{t('home.alertsSet')}</button>
              <NotificationToggle />
            </div>
          </div>
        )}
      </div>

      <div className="card flat">
        <h3>{t('home.read')}</h3>
        <LegendBody mode={mode} meta={grid?.grid} onExplain={(layer) => openScoreExplainer({ layer, meta: grid?.grid })} />
        <button className="btn sm quiet" type="button" onClick={() => openMethod(grid?.grid)}><Icon name="list" size="sm" />{t('explain.fullMethod')}</button>
      </div>

      <div className="row wrap small">
        <button className="btn sm quiet" type="button" onClick={openHowItWorks}><Icon name="info" size="sm" />{t('how.link')}</button>
        <a className="btn sm quiet" href="#/planner"><Icon name="dash" size="sm" />{t('about.planner')}</a>
        <a className="btn sm quiet" href={DOCS_URL} target="_blank" rel="noopener noreferrer"><Icon name="external" size="sm" />{t('about.api')}</a>
      </div>
      <p className="tiny muted">{t('how.notCrime')}</p>
    </div>
  );
}
