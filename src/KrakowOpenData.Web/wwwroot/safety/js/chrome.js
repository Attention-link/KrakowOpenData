// Shared page chrome: top bar, connection status pill, language menu, settings dialog, offline outbox.

import { h, icon, openDialog, toast, timeAgo, clear } from './util.js';
import { openMethod } from './explain.js';
import { state, set, on, isOffline } from './state.js';
import { t, setLang, LANGS, getLang } from './i18n.js';
import { kvGet, kvSet, kvClearAll, ls } from './db.js';
import { ping, postReport, ApiError, DOCS_URL } from './api.js';

// ── Connection monitoring ────────────────────────────────────────────────────
let pinger;

export function startConnectivity(onReconnect) {
  window.addEventListener('online', () => { set({ online: true }); check(); });
  window.addEventListener('offline', () => set({ online: false }));
  on('apiOk', (ok) => { if (ok) onReconnect?.(); else startPinger(); });
  on('online', (o) => { if (o) check(); });

  function startPinger() {
    clearInterval(pinger);
    pinger = setInterval(check, 20000);
  }

  async function check() {
    try {
      if (await ping()) {
        clearInterval(pinger);
        if (!state.apiOk) set({ apiOk: true });
      }
    } catch { /* still offline */ }
  }
}

// ── Outbox: reports written while offline are kept and sent later ────────────
export async function outboxList() { return (await kvGet('outbox')) || []; }

export async function outboxAdd(item) {
  const list = await outboxList();
  list.push({ ...item, queuedAt: Date.now() });
  await kvSet('outbox', list);
  emitOutbox(list.length);
  return list.length;
}

const outboxListeners = new Set();
export const onOutbox = (fn) => { outboxListeners.add(fn); return () => outboxListeners.delete(fn); };
function emitOutbox(n) { outboxListeners.forEach((fn) => fn(n)); }

let flushing = false;
/** Sends queued reports. Items the server rejects for good (400/429) are dropped with a notice. */
export async function flushOutbox() {
  if (flushing || isOffline()) return 0;
  flushing = true;
  let sent = 0;
  try {
    const list = await outboxList();
    const keep = [];
    for (const item of list) {
      try {
        await postReport(item.body);
        sent++;
      } catch (e) {
        if (e instanceof ApiError && e.status >= 400 && e.status < 500) {
          toast(t('outbox.dropped'), { error: true });
        } else {
          keep.push(item);
        }
      }
    }
    await kvSet('outbox', keep);
    emitOutbox(keep.length);
    if (sent) toast(t('outbox.sent', { n: sent }));
  } finally {
    flushing = false;
  }
  return sent;
}

// ── Status pill ──────────────────────────────────────────────────────────────
export function statusPill({ savedAt } = {}) {
  const pill = h('span', { class: 'status-pill', role: 'status' });
  let pending = 0;
  let saved = savedAt || null;

  const render = () => {
    clear(pill);
    const off = isOffline();
    pill.className = `status-pill ${off ? 'off' : ''}`;
    pill.append(icon(off ? 'offline' : 'online', 'sm'), h('span', { class: 'hide-sm' }, off ? t('net.offline') : t('net.online')));
    if (off && saved) pill.append(h('span', { class: 'tiny hide-sm' }, `· ${timeAgo(saved, t, getLang())}`));
    if (pending) pill.append(h('span', { class: 'chip warn' }, t('outbox.pending', { n: pending })));
    pill.title = off ? t('net.offlineHint') : t('net.onlineHint');
  };

  const offs = [on('online', render), on('apiOk', render), on('lang', render), onOutbox((n) => { pending = n; render(); })];
  outboxList().then((l) => { pending = l.length; render(); });
  render();
  pill.setSaved = (ts) => { saved = ts; render(); };
  pill.destroy = () => offs.forEach((off) => off());
  return pill;
}

// ── Language select ──────────────────────────────────────────────────────────
export function langSelect() {
  const sel = h('select', { class: 'lang-select', id: 'lang-select', 'aria-label': t('lang.label'), style: { minHeight: '36px', width: 'auto', padding: '.2rem .5rem' } },
    LANGS.map(([code, name]) => h('option', { value: code, selected: code === state.lang }, name)));
  sel.addEventListener('change', () => setLang(sel.value));
  return sel;
}

/** The top bar: brand, connection status, language. (There is no settings screen: everything a user needs is on the page.) */
export function topbar({ subtitle, right = [], pill } = {}) {
  return h('header', { class: 'topbar' },
    h('a', { class: 'brand', href: '#/', 'aria-label': t('app.title') },
      h('img', { src: 'icon.svg', alt: '' }),
      h('div', { class: 'truncate' }, h('b', null, t('app.title')), h('span', null, subtitle || t('app.subtitle')))),
    h('div', { class: 'spacer' }),
    // The Kompas Krakowa home page, outside this app: the browser loads it (not this app's router).
    h('a', { class: 'btn sm quiet', href: '/', target: '_top', title: t('nav.homeHint') }, icon('home', 'sm'), h('span', { class: 'hide-sm' }, t('nav.home'))),
    pill || '',
    ...right,
    h('button', { class: 'btn sm quiet', type: 'button', 'aria-label': t('display.short'), title: t('display.open'), onclick: openAccessibilityMenu },
      icon('a11y', 'sm'), h('span', { class: 'hide-sm' }, t('display.short'))),
    langSelect());
}

// ── Display settings (high contrast, larger text) ────────────────────────────
// Saved as localStorage 'display' = {contrast, large}; js/display-boot.js applies them before the first paint.
export function applyDisplay(d = ls.get('display') || {}) {
  const root = document.documentElement;
  if (d.contrast) root.dataset.contrast = 'high'; else delete root.dataset.contrast;
  if (d.large) root.dataset.text = 'large'; else delete root.dataset.text;
}

/** The "Display" toggle group: two switches, each with aria-pressed. */
export function displayToggles() {
  const toggle = (key, label) => {
    const btn = h('button', { class: 'btn sm toggle', type: 'button', 'data-fk': `display-${key}` });
    const paint = () => {
      const on = !!(ls.get('display') || {})[key];
      btn.setAttribute('aria-pressed', String(on));
      btn.replaceChildren(icon(on ? 'check' : 'plus', 'sm'), label);
    };
    btn.addEventListener('click', () => {
      const d = { ...(ls.get('display') || {}) };
      d[key] = !d[key];
      ls.set('display', d);
      applyDisplay(d);
      paint();
    });
    paint();
    return btn;
  };
  return h('div', { class: 'stack tight' },
    h('div', { class: 'row wrap', role: 'group', 'aria-label': t('display.title') }, toggle('contrast', t('display.contrast')), toggle('large', t('display.large'))),
    h('p', { class: 'tiny muted' }, t('display.help')));
}

/** The accessibility button in the top bar: display settings and the accessibility statement. */
export function openAccessibilityMenu() {
  openDialog((close) => ({
    title: t('display.open'),
    body: h('div', { class: 'stack' },
      h('h3', null, t('display.title')), displayToggles(),
      h('p', null, h('a', { href: '#/accessibility', onclick: () => close('x') }, icon('a11y', 'sm'), ' ', t('stmt.link')))),
    footer: h('button', { class: 'btn primary', type: 'button', onclick: () => close('ok') }, t('common.done'))
  }));
}

// ── Read aloud ───────────────────────────────────────────────────────────────
const VOICE_LANG = { en: 'en-GB', pl: 'pl-PL', uk: 'uk-UA' };
let speakingKey = null;

/** A "Read aloud" button that speaks getText() in the app language. Null when the browser cannot speak (then nothing is shown). */
export function readAloudButton(getText, key) {
  if (!('speechSynthesis' in window) || typeof SpeechSynthesisUtterance === 'undefined') return null;
  const btn = h('button', { class: 'btn sm', type: 'button', 'data-fk': `read-${key}`, 'data-read': key });
  // The panel may be rebuilt while speaking: every button with the same key shows the same state.
  btn.paint = () => {
    const on = speakingKey === key;
    btn.replaceChildren(icon(on ? 'x' : 'speaker', 'sm'), on ? t('read.stop') : t('read.aloud'));
  };
  btn.addEventListener('click', () => {
    const wasMe = speakingKey === key;
    stopReading();
    if (wasMe) return;
    const lang = getLang();
    const u = new SpeechSynthesisUtterance(getText());
    u.lang = VOICE_LANG[lang] || lang;
    const voice = speechSynthesis.getVoices().find((v) => (v.lang || '').toLowerCase().startsWith(lang));
    if (voice) u.voice = voice;
    u.onend = u.onerror = () => { if (speakingKey === key) { speakingKey = null; repaintReaders(); } };
    speakingKey = key;
    repaintReaders();
    speechSynthesis.speak(u);
  });
  btn.paint();
  return btn;
}

function repaintReaders() { document.querySelectorAll('[data-read]').forEach((b) => b.paint?.()); }

export function stopReading() {
  if (!('speechSynthesis' in window)) return;
  speakingKey = null;
  speechSynthesis.cancel();
  repaintReaders();
}

/** A switch for device notifications about alerts; asks the browser for permission when turned on. */
export function notificationToggle() {
  const btn = h('button', { class: 'btn sm', type: 'button' });
  const paint = () => {
    btn.setAttribute('aria-pressed', String(state.notify));
    btn.replaceChildren(icon('bell', 'sm'), state.notify ? t('notify.on') : t('notify.off'));
  };
  btn.addEventListener('click', async () => {
    if (state.notify) { set({ notify: false }); paint(); return; }
    if (!('Notification' in window)) { toast(t('notify.unsupported'), { error: true }); return; }
    const result = await Notification.requestPermission();
    if (result === 'granted') { set({ notify: true }); toast(t('notify.granted')); }
    else toast(t('notify.denied'), { error: true });
    paint();
  });
  paint();
  return btn;
}

// ── Theme ────────────────────────────────────────────────────────────────────
export function applyTheme() {
  const th = state.theme;
  if (th === 'light' || th === 'dark') document.documentElement.dataset.theme = th;
  else delete document.documentElement.dataset.theme;
}

// ── Explanation of the scores (resident-facing) ──────────────────────────────
export function openHowItWorks() {
  openDialog((close) => ({
    title: t('how.title'),
    body: h('div', { class: 'stack' },
      h('p', null, t('how.intro')),
      h('div', null, h('h3', null, h('span', { class: 'chip safety' }, icon('moon', 'sm'), t('mode.safety'))), h('p', { class: 'small' }, t('how.safety'))),
      h('div', null, h('h3', null, h('span', { class: 'chip heat' }, icon('sun', 'sm'), t('mode.heat'))), h('p', { class: 'small' }, t('how.heat'))),
      h('div', null, h('h3', null, h('span', { class: 'chip flood' }, icon('wave', 'sm'), t('mode.flood'))), h('p', { class: 'small' }, t('how.flood'))),
      h('div', null, h('h3', null, h('span', { class: 'chip air' }, icon('wind', 'sm'), t('mode.air'))), h('p', { class: 'small' }, t('how.air'))),
      h('p', { class: 'small' }, t('how.reports')),
      h('p', { class: 'banner info small' }, icon('info'), h('span', null, t('how.notCrime')))),
    footer: [h('button', { class: 'btn', type: 'button', onclick: () => { close('x'); openMethod(); } }, icon('list', 'sm'), t('explain.fullMethod')),
      h('a', { class: 'btn', href: DOCS_URL, target: '_blank', rel: 'noopener' }, icon('external', 'sm'), t('about.api')),
      h('button', { class: 'btn primary', type: 'button', onclick: () => close('ok') }, t('common.done'))]
  }));
}
