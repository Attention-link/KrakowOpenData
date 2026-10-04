// "Wyjaśnij prostym językiem": asks the AI Worker (/ai/explain, same origin) to put the score shown in the dialog into a few plain
// sentences. It sends only the numbers already on screen; the Worker is told to use nothing else, to say "brak danych" for gaps
// and to keep resident reports apart. Any failure shows "AI chwilowo niedostępne" and the table stays as it is: the app works
// fully without the Worker. Hidden when window.KRK_CONFIG.ai === false.

import { h, formatDistance } from './util.js';
import { t, getLang } from './i18n.js';
import { aiExplain } from './api.js';
import { LAYERS } from './model.js';

const enabled = () => (window.KRK_CONFIG || {}).ai !== false;

function payload(layer, place, method, meta) {
  const keys = layer === 'both' ? ['safety', 'heat'] : [layer];
  const info = (key) => method?.layers.flatMap((l) => l.factors).find((f) => f.key === key) || null;
  const radius = formatDistance(meta?.searchRadiusMeters || 1500);
  const factors = [];
  for (const key of keys) {
    const data = place[key];
    if (!data) continue;
    for (const f of data.factors) {
      factors.push({
        label: `${t(`mode.${LAYERS[key] ? key : 'safety'}`)}: ${t(`factor.${f.key}`)}`,
        // A missing distance means "none found within the search radius" (a fact), not missing data.
        value: f.unit === 'm' ? (f.value === null || f.value === undefined ? t('factor.none', { r: radius }) : Math.round(f.value)) : Math.round(f.value ?? 0),
        unit: f.unit,
        source: info(f.key)?.source
      });
    }
  }
  const d = layer === 'both' ? null : place[layer];
  const reports = place.reports || [];
  return {
    lang: getLang(),
    kind: 'place',
    title: place.label || place.cellId,
    score: layer === 'both' ? place.combined : d?.score,
    band: layer === 'both' ? place.combinedBand : d?.band,
    generatedAt: place.generatedAt,
    factors: factors.slice(0, 20),
    reports: { verified: reports.filter((r) => r.verifiedByPlanner).length, unverified: reports.filter((r) => !r.verifiedByPlanner).length },
    dataGaps: (method?.limits || []).slice(0, 3),
    sources: [...new Set(factors.map((f) => f.source).filter(Boolean))].slice(0, 8)
  };
}

/** A section for the score dialog, or null when AI is switched off or there is no place to explain. */
export function aiExplainSection({ layer, place, method, meta = null }) {
  if (!enabled() || !place) return null;
  const out = h('div', { class: 'card flat small', role: 'status', 'aria-live': 'polite', 'aria-label': t('ai.generated'), hidden: true });
  const btn = h('button', { class: 'btn sm', type: 'button' }, t('ai.explain'));
  btn.addEventListener('click', async () => {
    btn.disabled = true;
    out.hidden = false;
    out.replaceChildren(h('span', { class: 'muted' }, t('ai.loading')));
    try {
      const r = await aiExplain(payload(layer, place, method, meta));
      out.replaceChildren(h('b', { class: 'tiny' }, t('ai.generated')), h('p', { style: { whiteSpace: 'pre-line', marginTop: '.25rem' } }, r.text));
    } catch {
      out.replaceChildren(h('span', null, t('ai.unavailable')));
    } finally {
      btn.disabled = false;
    }
  });
  return h('section', { class: 'explain-sec' }, h('div', { class: 'row wrap' }, btn), out);
}
