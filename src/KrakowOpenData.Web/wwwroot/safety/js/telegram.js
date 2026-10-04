// "Powiadomienia w Telegramie": links this app (its anonymous device id) to the resident's own Telegram chat, so alerts for their
// area and news about their reports (received, verified, resolved) arrive even when the app is closed. The server makes a one-time
// link (valid 15 minutes); opening it in Telegram and pressing Start completes the link. Hidden when the server has no bot.

import { h, icon, toast } from './util.js';
import { state } from './state.js';
import { t } from './i18n.js';
import { getTelegramStatus, linkTelegram, unlinkTelegram, errorText } from './api.js';

// The home menu is rebuilt often (alerts poll, language, view changes); share one status request for a minute instead of
// asking the server on every rebuild. Connect / disconnect / "check" ask again.
const STATUS_TTL_MS = 60_000;
let statusCache = null; // { at, promise }
function cachedStatus(force) {
  if (force || !statusCache || Date.now() - statusCache.at > STATUS_TTL_MS) {
    const promise = getTelegramStatus();
    statusCache = { at: Date.now(), promise };
    promise.catch(() => { if (statusCache?.promise === promise) statusCache = null; });
  }
  return statusCache.promise;
}

/** A card for the resident home menu. Starts hidden and removes itself when the feature is off or the API cannot be reached. */
export function telegramCard() {
  const card = h('div', { class: 'card flat', hidden: true });
  const body = h('div', { class: 'stack tight', 'aria-live': 'polite' });
  card.append(h('h3', null, t('tg.title')), body);

  const paint = (...nodes) => body.replaceChildren(...nodes);

  async function refresh(force = false) {
    try {
      const s = await cachedStatus(force === true);
      if (!s?.available) { card.remove(); return; }
      card.hidden = false;
      s.linked ? showLinked() : showOff();
    } catch {
      card.remove();
    }
  }

  function showOff() {
    paint(h('p', { class: 'small muted' }, t('tg.help')),
      h('div', { class: 'row wrap' }, h('button', { class: 'btn sm', type: 'button', onclick: connect }, icon('send', 'sm'), t('tg.connect'))));
  }

  function showLinked() {
    paint(h('p', { class: 'small' }, icon('check', 'sm'), ' ', t('tg.linked')),
      h('div', { class: 'row wrap' }, h('button', { class: 'btn sm quiet', type: 'button', onclick: disconnect }, t('tg.unlink'))));
  }

  async function connect() {
    try {
      const r = await linkTelegram(state.me?.lat, state.me?.lon);
      statusCache = null;
      const time = new Date(r.expiresAt).toLocaleTimeString(state.lang, { hour: '2-digit', minute: '2-digit' });
      paint(h('p', { class: 'small' }, t('tg.step')),
        h('div', { class: 'row wrap' },
          h('a', { class: 'btn sm primary', href: r.link, target: '_blank', rel: 'noopener noreferrer' }, icon('external', 'sm'), t('tg.open')),
          h('button', { class: 'btn sm', type: 'button', onclick: () => refresh(true) }, icon('refresh', 'sm'), t('tg.check'))),
        h('p', { class: 'tiny muted' }, t('tg.expires', { time })));
    } catch (e) {
      toast(errorText(e, t), { error: true });
    }
  }

  async function disconnect() {
    try {
      await unlinkTelegram();
    } catch (e) {
      // 404 means it was already unlinked; anything else did not unlink, so say so instead of "unlinked".
      if (e?.status !== 404) { toast(errorText(e, t), { error: true }); return; }
    }
    statusCache = null;
    toast(t('tg.unlinked'));
    showOff();
  }

  refresh();
  return card;
}
