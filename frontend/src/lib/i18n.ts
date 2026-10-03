// Translations: English, Polish, Ukrainian. Entries are [en, pl, uk]; a missing translation falls back to English.
// Later dictionaries override earlier ones. Planner screens are in English and Polish; Ukrainian falls back to English there.

import { useApp } from './store';
import type { Lang } from './types';
import { residentStrings } from '../i18n/strings-resident';
import { residentStringsV2 } from '../i18n/strings-resident-v2';
import { residentStringsV3 } from '../i18n/strings-resident-v3';
import { plannerStrings } from '../i18n/strings-planner';
import { plannerStringsV2 } from '../i18n/strings-planner-v2';

export const LANGS: [Lang, string][] = [['en', 'English'], ['pl', 'Polski'], ['uk', 'Українська']];
const INDEX: Record<Lang, number> = { en: 0, pl: 1, uk: 2 };
const dict = { ...residentStrings, ...residentStringsV2, ...residentStringsV3, ...plannerStrings, ...plannerStringsV2 } as Record<string, string[]>;
const warned = new Set<string>();

export type Params = Record<string, string | number>;
export type TFn = (key: string, params?: Params) => string;

export function translate(lang: Lang, key: string, params?: Params): string {
  const entry = dict[key];
  if (!entry) {
    if (!warned.has(key)) { warned.add(key); console.warn('Missing translation:', key); }
    return key;
  }
  let s = entry[INDEX[lang]] ?? entry[0];
  if (params) for (const [k, v] of Object.entries(params)) s = s.split(`{${k}}`).join(String(v));
  return s;
}

/** Translate with the current language (outside React). */
export const t: TFn = (key, params) => translate(useApp.getState().lang, key, params);

/** Translate into a specific language (texts written for agencies are usually Polish). */
export const tIn = (lang: Lang, key: string, params?: Params) => translate(lang, key, params);

/** Hook: re-renders when the language changes and returns the translate function. */
export function useT(): TFn {
  const lang = useApp((s) => s.lang);
  return (key, params) => translate(lang, key, params);
}

export const useLang = () => useApp((s) => s.lang);
