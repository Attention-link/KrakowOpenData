import { describe, expect, it, vi } from 'vitest';
import { LANGS, t, tIn, translate } from './i18n';
import { setApp } from './store';

describe('translate', () => {
  it('returns the text of the language', () => {
    expect(translate('en', 'mode.heat')).toBe('Heat');
    expect(translate('pl', 'mode.heat')).toBe('Upał');
    expect(translate('uk', 'mode.heat')).toBe('Спека');
  });

  it('falls back to English when a language has no text', () => {
    // The newest strings have English and Polish only for some keys: Ukrainian must still read something.
    expect(translate('en', 'kpi.cells')).toBe('Squares scored');
    expect(translate('uk', 'kpi.cells')).toBeTruthy();
    expect(translate('uk', 'kpi.cells')).not.toBe('kpi.cells');
  });

  it('fills in placeholders', () => {
    expect(translate('en', 'explain.addsHeat', { n: 12.5 })).toBe('adds 12.5 points of heat');
  });

  it('returns the key (and warns once) when the key is unknown', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    expect(translate('en', 'no.such.key')).toBe('no.such.key');
    expect(translate('en', 'no.such.key')).toBe('no.such.key');
    expect(warn).toHaveBeenCalledTimes(1);
    warn.mockRestore();
  });

  it('uses the current language for t() and a given one for tIn()', () => {
    setApp({ lang: 'pl' });
    expect(t('mode.heat')).toBe('Upał');
    expect(tIn('en', 'mode.heat')).toBe('Heat');
    setApp({ lang: 'en' });
  });

  it('lists the three languages', () => {
    expect(LANGS.map(([c]) => c)).toEqual(['en', 'pl', 'uk']);
  });
});

describe('the score wording', () => {
  it('says which way each score points', () => {
    expect(translate('en', 'explain.dir.heat')).toBe('Higher = hotter');
    expect(translate('en', 'explain.dir.safety')).toBe('Higher = safer');
  });
  it('has heat band names for all four bands', () => {
    for (const b of ['Good', 'Fair', 'Weak', 'Critical']) expect(translate('en', `band.heat.${b}`)).not.toBe(`band.heat.${b}`);
  });
});
