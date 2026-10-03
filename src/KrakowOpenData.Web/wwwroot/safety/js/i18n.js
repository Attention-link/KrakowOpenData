// Translations: English, Polish, Ukrainian. Entries are [en, pl, uk]; a missing translation falls back to English.
// Planner screens are translated into English and Polish (city staff); Ukrainian falls back to English there.

import { state, set, on } from './state.js';
import { residentStrings } from './strings-resident.js';
import { residentStringsV2 } from './strings-resident-v2.js';
import { residentStringsV3 } from './strings-resident-v3.js';
import { plannerStrings } from './strings-planner.js';
import { plannerStringsV2 } from './strings-planner-v2.js';
import { layerStrings } from './strings-layers.js';

export const LANGS = [['en', 'English'], ['pl', 'Polski'], ['uk', 'Українська']];
const INDEX = { en: 0, pl: 1, uk: 2 };
const dict = { ...residentStrings, ...residentStringsV2, ...residentStringsV3, ...plannerStrings, ...plannerStringsV2, ...layerStrings };
const warned = new Set();

export function t(key, params) {
  const entry = dict[key];
  if (!entry) {
    if (!warned.has(key)) { warned.add(key); console.warn('Missing translation:', key); }
    return key;
  }
  let s = entry[INDEX[state.lang]] ?? entry[0];
  if (params) for (const [k, v] of Object.entries(params)) s = s.replaceAll(`{${k}}`, String(v));
  return s;
}

export const getLang = () => state.lang;

export function setLang(lang) {
  set({ lang });
  document.documentElement.lang = lang;
}

export const onLang = (fn) => on('lang', fn);

export function applyDocumentLanguage() {
  document.documentElement.lang = state.lang;
  document.title = `${t('app.title')} · Kraków`;
  const skip = document.querySelector('[data-i18n="skip"]');
  if (skip) skip.textContent = t('skip');
}

/** Translate into a specific language (used for texts written for agencies, which are usually Polish). */
export function tIn(lang, key, params) {
  const entry = dict[key];
  if (!entry) return key;
  let s = entry[INDEX[lang]] ?? entry[0];
  if (params) for (const [k, v] of Object.entries(params)) s = s.replaceAll(`{${k}}`, String(v));
  return s;
}
