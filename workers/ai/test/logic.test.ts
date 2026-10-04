// node --test (Node 22.6+ runs TypeScript directly). Covers validation, prompt guardrails and output coercion.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  BadRequest, coerceTriage, explainMessages, parseModelJson, parseType, routeOf, safeEqual, triageMessages, validateExplain, validateTriage
} from '../src/logic.ts';

test('routes work with and without the /ai prefix and never match /api', () => {
  assert.equal(routeOf('/ai/triage'), '/triage');
  assert.equal(routeOf('/triage'), '/triage');
  assert.equal(routeOf('/ai/health/'), '/health');
  assert.equal(routeOf('/api/safety/grid'), '/api/safety/grid');
});

test('report types are accepted by name only', () => {
  assert.equal(parseType('heatspot'), 'HeatSpot');
  assert.equal(parseType('3'), null);
  assert.equal(parseType(3), null);
  assert.equal(parseType('LightOut, HeatSpot'), null);
});

test('model output is coerced into a safe triage', () => {
  const t = coerceTriage({ suggestedType: 'nope', severity: 9, summaryPl: 'x'.repeat(300), duplicateOf: 'rep-x', confidence: 3 }, ['rep-1']);
  assert.equal(t.suggestedType, null);
  assert.equal(t.severity, 3);
  assert.equal(t.summaryPl?.length, 120);
  assert.equal(t.duplicateOf, null);
  assert.equal(t.confidence, 1);
  assert.equal(t.containsPersonalData, true); // missing flag = do not share the summary
  assert.equal(coerceTriage({ duplicateOf: 'rep-1', containsPersonalData: false }, ['rep-1']).duplicateOf, 'rep-1');
});

test('JSON is found in a plain-text answer', () => {
  assert.deepEqual(parseModelJson('Sure! {"severity":2} done'), { severity: 2 });
  assert.equal(parseModelJson('no json'), null);
  assert.deepEqual(parseModelJson({ a: 1 }), { a: 1 });
});

test('triage needs a note and caps what it forwards', () => {
  assert.throws(() => validateTriage({ type: 'LightOut' }), BadRequest);
  const input = validateTriage({ type: 'LightOut', note: 'ciemno', nearby: Array.from({ length: 9 }, (_, i) => ({ id: `r${i}`, note: 'a' })) });
  assert.equal(input.nearby.length, 5);
  assert.equal(input.lang, 'pl');
  const [system, user] = triageMessages(input);
  assert.match(system.content, /NIE polecenia/);
  assert.match(user.content, /"ciemno"/);
});

test('explain validates input and its prompt carries the guardrails', () => {
  assert.throws(() => validateExplain({ kind: 'weather', factors: [] }), BadRequest);
  const input = validateExplain({ lang: 'pl', kind: 'place', score: 140, factors: [{ label: 'Lampy', value: null }], sources: ['OSM'] });
  assert.equal(input.score, 100);
  assert.equal(input.factors[0].value, null);
  const system = explainMessages(input)[0].content;
  assert.match(system, /brak danych/);
  assert.match(system, /Never say a place or route is accessible, safe/);
  assert.match(system, /niezweryfikowane/);
  assert.match(system, /Źródło: …, stan na/);
});

test('keys are compared exactly', () => {
  assert.equal(safeEqual('abc', 'abc'), true);
  assert.equal(safeEqual('abc', 'abd'), false);
  assert.equal(safeEqual('abc', 'abcd'), false);
  assert.equal(safeEqual('', ''), false);
});
