// The AI suggestion on a report row (planners only): "Sugestia AI · niezweryfikowane", severity, the short summary and flags.
// Advice, never a decision: it does not change the score, the type or the verification. Text is set as text, never as HTML.

import { h } from './util.js';
import { t } from './i18n.js';

export function aiSuggestion(r) {
  const ai = r?.triage;
  if (!ai) return null;
  const sev = Math.min(3, Math.max(1, Math.round(ai.severity || 1)));
  const parts = [t('ai.severity', { n: sev })];
  if (ai.suggestedType && ai.suggestedType !== r.type) parts.push(t('ai.otherType', { type: t(`rtype.${ai.suggestedType}`) }));
  if (ai.duplicateOf) parts.push(t('ai.duplicate'));
  if (ai.containsPersonalData) parts.push(t('ai.personal'));
  if (ai.isAbuse) parts.push(t('ai.abuse'));
  return h('div', { class: 'small', style: { marginTop: '.25rem' } },
    h('span', { class: `chip ${sev === 3 ? 'danger' : 'warn'}`, title: t('ai.disclaimer') }, t('ai.suggestion')), ' ',
    h('span', { class: 'muted' }, parts.join(' · ')),
    ai.summaryPl ? h('p', { class: 'small', style: { marginTop: '.15rem' } }, `„${ai.summaryPl}”`) : null);
}
