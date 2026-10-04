// Small DOM and formatting helpers. Everything user-supplied goes through textContent, never innerHTML.

const SVG_NS = 'http://www.w3.org/2000/svg';

/** h('div', {class:'x', onclick: fn, 'aria-label': 'y'}, child, [children]) -> HTMLElement */
export function h(tag, props, ...children) {
  const el = document.createElement(tag);
  if (props) {
    for (const [k, v] of Object.entries(props)) {
      if (v === undefined || v === null || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
      else if (k === 'dataset') Object.assign(el.dataset, v);
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
      else if (v === true) el.setAttribute(k, '');
      else el.setAttribute(k, v);
    }
  }
  append(el, children);
  return el;
}

export function append(el, children) {
  for (const c of children.flat(Infinity)) {
    if (c === null || c === undefined || c === false) continue;
    el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  }
  return el;
}

export function clear(el) {
  while (el.firstChild) el.removeChild(el.firstChild);
  return el;
}

export function icon(name, cls = '') {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('class', `i ${cls}`.trim());
  svg.setAttribute('aria-hidden', 'true');
  const use = document.createElementNS(SVG_NS, 'use');
  use.setAttribute('href', `#i-${name}`);
  svg.append(use);
  return svg;
}

export function svg(tag, attrs = {}, ...children) {
  const el = document.createElementNS(SVG_NS, tag);
  for (const [k, v] of Object.entries(attrs)) if (v !== undefined && v !== null) el.setAttribute(k, v);
  for (const c of children.flat(Infinity)) if (c) el.append(c instanceof Node ? c : document.createTextNode(String(c)));
  return el;
}

export const $ = (sel, root = document) => root.querySelector(sel);

export function debounce(fn, ms) {
  let t;
  return (...args) => {
    clearTimeout(t);
    t = setTimeout(() => fn(...args), ms);
  };
}

export function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID();
  const b = crypto.getRandomValues(new Uint8Array(16));
  return [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
}

export const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

export function haversine(a, b) {
  const R = 6371000, rad = (d) => (d * Math.PI) / 180;
  const dLat = rad(b[0] - a[0]), dLon = rad(b[1] - a[1]);
  const x = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a[0])) * Math.cos(rad(b[0])) * Math.sin(dLon / 2) ** 2;
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(x)));
}

export function formatDistance(m) {
  if (m === null || m === undefined) return '–';
  return m >= 1000 ? `${(m / 1000).toFixed(1)} km` : `${Math.round(m)} m`;
}

/** Announces a message to screen readers. */
export function announce(message) {
  const live = document.getElementById('live');
  if (!live) return;
  live.textContent = '';
  setTimeout(() => { live.textContent = message; }, 50);
}

/** True when the user asked the system for less motion (no map fly / pan animations then). */
export const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;

/**
 * Re-renders part of the page without losing keyboard focus. Elements that can hold focus carry a stable data-fk key;
 * after render() the element with the same key gets focus back (with the caret where it was). When focus was inside
 * `root` but its element is gone, it moves to `fallback` (e.g. the panel title) instead of falling to <body>.
 */
export function keepFocus(root, render, fallback = null) {
  const el = document.activeElement;
  const inside = !!el && el !== document.body && root.contains(el);
  const key = inside ? el.getAttribute('data-fk') : null;
  const caret = key && typeof el.selectionStart === 'number' ? [el.selectionStart, el.selectionEnd] : null;
  const out = render();
  if (inside && !root.contains(document.activeElement)) {
    const next = key ? root.querySelector(`[data-fk="${CSS.escape(key)}"]`) : null;
    if (next && !next.disabled) {
      next.focus({ preventScroll: true });
      if (caret && typeof next.setSelectionRange === 'function') { try { next.setSelectionRange(caret[0], caret[1]); } catch { /* not a text field */ } }
    } else fallback?.focus({ preventScroll: true });
  }
  return out;
}

/** Shows a toast. opts: {error, action:{label,onClick}, ms} */
export function toast(message, opts = {}) {
  const host = document.getElementById('toasts');
  const el = h('div', { class: `toast${opts.error ? ' err' : ''}`, role: opts.error ? 'alert' : 'status' },
    h('div', { class: 'grow' }, message),
    opts.action ? h('button', { type: 'button', onclick: () => { opts.action.onClick(); el.remove(); } }, opts.action.label) : null);
  host.append(el);
  announce(message);
  setTimeout(() => el.remove(), opts.ms ?? (opts.error ? 7000 : 4500));
  return el;
}

// Texts util.js needs but cannot translate itself (i18n imports state, which imports util): main.js plugs in t().
let uiText = (key) => ({ 'common.close': 'Close' })[key] || key;
export function setUiText(fn) { uiText = fn; }

let dialogCount = 0;
/** Opens a modal <dialog>. Returns {dialog, close}. `build(close)` returns {title, body, footer}. */
export function openDialog(build, { onClose } = {}) {
  const dialog = document.createElement('dialog');
  const close = (value) => { dialog.close(value); };
  const { title, body, footer } = build(close);
  const titleId = `dlg-title-${++dialogCount}`;
  dialog.setAttribute('aria-labelledby', titleId);
  const closeBtn = h('button', { class: 'btn icon quiet', type: 'button', 'aria-label': uiText('common.close'), onclick: () => close('x') }, icon('x'));
  dialog.append(
    h('div', { class: 'dlg-head' }, h('h2', { class: 'grow', id: titleId }, title), closeBtn),
    h('div', { class: 'dlg-body' }, body),
    footer ? h('div', { class: 'dlg-foot' }, footer) : '');
  dialog.addEventListener('close', () => { dialog.remove(); onClose?.(dialog.returnValue); });
  dialog.addEventListener('click', (e) => { if (e.target === dialog) close('backdrop'); });
  document.body.append(dialog);
  dialog.showModal();
  return { dialog, close };
}

export function timeAgo(date, t, lang) {
  const d = new Date(date);
  const s = Math.round((Date.now() - d.getTime()) / 1000);
  if (s < 45) return t('time.now');
  const m = Math.round(s / 60);
  if (m < 60) return t('time.minAgo', { n: m });
  const hrs = Math.round(m / 60);
  if (hrs < 36) return t('time.hAgo', { n: hrs });
  return new Intl.DateTimeFormat(lang, { day: 'numeric', month: 'short' }).format(d);
}

export function timeLeft(date, t) {
  const m = Math.round((new Date(date).getTime() - Date.now()) / 60000);
  if (m <= 0) return t('time.expired');
  if (m < 90) return t('time.minLeft', { n: m });
  return t('time.hLeft', { n: Math.round(m / 60) });
}

export function clock(date, lang) {
  return new Intl.DateTimeFormat(lang, { hour: '2-digit', minute: '2-digit' }).format(new Date(date));
}

export function geoLink(lat, lon, label) {
  // geo: opens the default maps app on phones; the https link is the desktop fallback.
  return `https://www.openstreetmap.org/?mlat=${lat}&mlon=${lon}#map=17/${lat}/${lon}`;
}

export function directionsLink(lat, lon) {
  return `https://www.openstreetmap.org/directions?engine=fossgis_osrm_foot&route=%3B${lat}%2C${lon}`;
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const ta = h('textarea', { style: { position: 'fixed', opacity: 0 } });
    ta.value = text;
    document.body.append(ta);
    ta.select();
    let ok = false;
    try { ok = document.execCommand('copy'); } catch { /* ignore */ }
    ta.remove();
    return ok;
  }
}
