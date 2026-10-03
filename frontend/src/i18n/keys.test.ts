// Every text the screens ask for must exist: a missing translation shows the raw key to a resident.

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { residentStrings } from './strings-resident';
import { residentStringsV2 } from './strings-resident-v2';
import { residentStringsV3 } from './strings-resident-v3';
import { plannerStrings } from './strings-planner';
import { plannerStringsV2 } from './strings-planner-v2';

const dict: Record<string, string[]> = { ...residentStrings, ...residentStringsV2, ...residentStringsV3, ...plannerStrings, ...plannerStringsV2 };
const SRC = join(__dirname, '..');

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((f) => {
    const p = join(dir, f);
    if (statSync(p).isDirectory()) return f === 'i18n' || f === 'test' ? [] : files(p);
    return /\.(tsx?|ts)$/.test(f) && !/\.test\./.test(f) ? [p] : [];
  });
}

/** Keys written out in full, like t('mode.heat'). Keys built from a variable (t(`band.${b}`)) are checked by the families below. */
function literalKeys(): Map<string, string> {
  const found = new Map<string, string>();
  for (const file of files(SRC)) {
    const text = readFileSync(file, 'utf8');
    for (const m of text.matchAll(/\b(?:t|tt|tIn|L)\((?:'[a-z]{2}',\s*)?'([a-zA-Z0-9_]+(?:\.[a-zA-Z0-9_]+)+)'/g)) found.set(m[1], file);
  }
  return found;
}

describe('translations', () => {
  it('has an English text for every entry, and no entry is empty', () => {
    for (const [key, entry] of Object.entries(dict)) {
      expect(entry.length, key).toBeGreaterThanOrEqual(1);
      expect(entry[0].trim(), key).not.toBe('');
    }
  });

  it('has Polish text for the resident screens', () => {
    const missing = Object.entries({ ...residentStrings, ...residentStringsV2, ...residentStringsV3 }).filter(([, e]) => !e[1] || !e[1].trim()).map(([k]) => k);
    expect(missing).toEqual([]);
  });

  it('uses the same {placeholders} in every language of an entry', () => {
    const bad: string[] = [];
    for (const [key, entry] of Object.entries(dict)) {
      const sets = entry.map((s) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort().join(','));
      if (new Set(sets).size > 1) bad.push(`${key}: ${sets.join(' | ')}`);
    }
    expect(bad).toEqual([]);
  });

  it('has every key the screens ask for', () => {
    const missing = [...literalKeys()].filter(([key]) => !(key in dict)).map(([key, file]) => `${key} (${file.split('src')[1]})`);
    expect(missing).toEqual([]);
  });

  it('has the families of keys that are built from a variable', () => {
    const families: [string, string[]][] = [
      ['band.', ['Good', 'Fair', 'Weak', 'Critical']],
      ['band.heat.', ['Good', 'Fair', 'Weak', 'Critical']],
      ['mode.', ['safety', 'heat', 'both']],
      ['mode.%.score', ['safety', 'heat', 'both']],
      ['factor.', ['water', 'green', 'refuge', 'toilets', 'transit', 'lighting', 'nightTransit', 'openPlaces', 'aed']],
      ['rtype.', ['LightOut', 'UnsafeAtNight', 'PathHazard', 'WaterNotWorking', 'NoShade', 'HeatSpot']],
      ['kind.', ['DrinkingWater', 'Toilets', 'Park', 'Library', 'Pharmacy', 'Hospital', 'Police', 'Defibrillator', 'Stop']],
      ['route.', ['fastest', 'safest', 'coolest', 'balanced']],
      ['sev.', ['Info', 'Warning', 'Critical', 'high', 'medium']],
      ['event.', ['heat', 'night', 'both']],
      ['heat.', ['None', 'HotDay', 'WarningLevel1', 'WarningLevel2', 'WarningLevel3']],
      ['air.', ['Good', 'Moderate', 'Poor', 'VeryPoor', 'Unknown']]
    ];
    const missing: string[] = [];
    for (const [prefix, names] of families) for (const n of names) {
      const key = prefix.includes('%') ? prefix.replace('%', n) : prefix + n;
      if (!(key in dict)) missing.push(key);
    }
    expect(missing).toEqual([]);
  });

  it('has an alert-template text for residents in all three languages', () => {
    for (const tpl of ['al.tpl.heat.body', 'al.tpl.night.body']) expect(dict[tpl], tpl).toHaveLength(3);
  });
});
