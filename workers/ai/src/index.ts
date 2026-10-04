// Kompas Krakowa AI Worker (Cloudflare Workers AI). Routes (also without the /ai prefix, for workers.dev):
//   GET  /ai/health
//   POST /ai/explain     public, rate-limited per IP: plain-language explanation of a score from the facts given
//   POST /ai/triage      X-Ai-Key: category / severity / summary suggestion for a resident report
//   POST /ai/transcribe  X-Ai-Key: audio (≤ 1 MB / 60 s) → Whisper → text + triage
// Every answer is a suggestion; the API and the apps work without this Worker. Audio is never stored.

import {
  AUDIO_TYPES, BadRequest, EXPLAIN_MAX_TOKENS, MAX_AUDIO_BYTES, MAX_JSON_BYTES, TRIAGE_MAX_TOKENS, TRIAGE_SCHEMA,
  coerceTriage, explainMessages, parseModelJson, routeOf, safeEqual, sha256Hex, toBase64, triageMessages,
  validateExplain, validateTriage, type Triage, type TriageInput
} from './logic.ts';

interface AiBinding {
  run(model: string, inputs: Record<string, unknown>): Promise<unknown>;
}

interface RateLimiter {
  limit(options: { key: string }): Promise<{ success: boolean }>;
}

export interface Env {
  AI: AiBinding;
  LIMITER?: RateLimiter;
  CACHE?: KVNamespace;
  AI_KEY?: string;
  MODEL?: string;
  MODEL_EXPLAIN?: string;
  WHISPER_MODEL?: string;
  WHISPER_FALLBACK?: string;
  ALLOWED_ORIGIN?: string;
}

const DEFAULT_MODEL = '@cf/meta/llama-3.1-8b-instruct-fast';
const DEFAULT_WHISPER = '@cf/openai/whisper-large-v3-turbo';
const DEFAULT_WHISPER_FALLBACK = '@cf/openai/whisper';
const CACHE_TTL = 24 * 60 * 60;

const models = (env: Env) => ({
  triage: env.MODEL || DEFAULT_MODEL,
  explain: env.MODEL_EXPLAIN || env.MODEL || DEFAULT_MODEL,
  whisper: env.WHISPER_MODEL || DEFAULT_WHISPER,
  whisperFallback: env.WHISPER_FALLBACK || DEFAULT_WHISPER_FALLBACK
});

class HttpError extends Error {
  constructor(readonly status: number, message: string) { super(message); }
}

function json(body: unknown, status = 200, extra: HeadersInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff', ...extra }
  });
}

/** Same origin in production, so CORS is not needed; it is allowed only for the site itself. */
function cors(request: Request, env: Env): Record<string, string> {
  const allowed = env.ALLOWED_ORIGIN || 'https://opendata.al.mt';
  return request.headers.get('origin') === allowed
    ? { 'access-control-allow-origin': allowed, 'access-control-allow-methods': 'GET, POST, OPTIONS', 'access-control-allow-headers': 'content-type', vary: 'origin' }
    : {};
}

function requireKey(request: Request, env: Env): void {
  if (!env.AI_KEY || !safeEqual(request.headers.get('x-ai-key') ?? '', env.AI_KEY)) throw new HttpError(401, 'X-Ai-Key required');
}

async function readBytes(request: Request, max: number): Promise<Uint8Array> {
  const declared = Number(request.headers.get('content-length') ?? '0');
  if (declared > max) throw new HttpError(413, `Body larger than ${max} bytes`);
  const reader = request.body?.getReader();
  if (!reader) return new Uint8Array();
  const chunks: Uint8Array[] = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > max) throw new HttpError(413, `Body larger than ${max} bytes`);
    chunks.push(value);
  }
  const out = new Uint8Array(size);
  let at = 0;
  for (const c of chunks) { out.set(c, at); at += c.byteLength; }
  return out;
}

async function readJson(request: Request): Promise<unknown> {
  const bytes = await readBytes(request, MAX_JSON_BYTES);
  try {
    return JSON.parse(new TextDecoder().decode(bytes));
  } catch {
    throw new BadRequest('Body must be JSON.');
  }
}

/** KV cache (24 h) keyed by sha256(model + path + body); skipped when the CACHE binding is absent. */
async function cached<T>(env: Env, model: string, path: string, body: unknown, run: () => Promise<T>): Promise<{ value: T; cached: boolean }> {
  if (!env.CACHE) return { value: await run(), cached: false };
  const key = `v1:${await sha256Hex(model + path + JSON.stringify(body))}`;
  const hit = await env.CACHE.get<T>(key, 'json').catch(() => null);
  if (hit !== null) return { value: hit, cached: true };
  const value = await run();
  await env.CACHE.put(key, JSON.stringify(value), { expirationTtl: CACHE_TTL }).catch(() => undefined);
  return { value, cached: false };
}

async function runTriage(env: Env, input: TriageInput): Promise<Triage> {
  const model = models(env).triage;
  const { value } = await cached(env, model, '/triage', input, async () => {
    const out = (await env.AI.run(model, {
      messages: triageMessages(input),
      response_format: { type: 'json_schema', json_schema: TRIAGE_SCHEMA },
      max_tokens: TRIAGE_MAX_TOKENS,
      temperature: 0.1
    })) as { response?: unknown };
    const raw = parseModelJson(out?.response);
    if (!raw) throw new HttpError(502, 'The model did not return JSON');
    return coerceTriage(raw, input.nearby.map((n) => n.id));
  });
  return value;
}

async function transcribe(env: Env, audio: Uint8Array): Promise<{ text: string; language: string | null }> {
  const m = models(env);
  const read = (out: unknown) => {
    const o = (out ?? {}) as { text?: unknown; transcription_info?: { text?: unknown; language?: unknown } };
    const text = typeof o.text === 'string' ? o.text : typeof o.transcription_info?.text === 'string' ? o.transcription_info.text : '';
    const language = typeof o.transcription_info?.language === 'string' ? o.transcription_info.language : null;
    return { text: text.trim(), language };
  };
  try {
    return read(await env.AI.run(m.whisper, { audio: toBase64(audio), task: 'transcribe', vad_filter: true }));
  } catch {
    // The older Whisper takes the bytes as an array of numbers.
    return read(await env.AI.run(m.whisperFallback, { audio: [...audio] }));
  }
}

async function handle(request: Request, env: Env): Promise<Response> {
  const route = routeOf(new URL(request.url).pathname);
  const c = cors(request, env);
  if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: c });

  if (route === '/health' && request.method === 'GET') return json({ ok: true, models: models(env), cache: !!env.CACHE }, 200, c);
  if (request.method !== 'POST') return json({ error: 'Not found' }, 404, c);

  if (route === '/explain') {
    const ip = request.headers.get('cf-connecting-ip') ?? 'unknown';
    if (env.LIMITER && !(await env.LIMITER.limit({ key: `explain:${ip}` })).success) return json({ error: 'Too many requests' }, 429, { ...c, 'retry-after': '60' });
    const input = validateExplain(await readJson(request));
    const model = models(env).explain;
    const { value, cached: hit } = await cached(env, model, '/explain', input, async () => {
      const out = (await env.AI.run(model, { messages: explainMessages(input), max_tokens: EXPLAIN_MAX_TOKENS, temperature: 0.2 })) as { response?: unknown };
      const text = typeof out?.response === 'string' ? out.response.trim() : '';
      if (!text) throw new HttpError(502, 'The model returned no text');
      return text;
    });
    return json({ text: value, model, cached: hit }, 200, c);
  }

  if (route === '/triage') {
    requireKey(request, env);
    const input = validateTriage(await readJson(request));
    return json(await runTriage(env, input));
  }

  if (route === '/transcribe') {
    requireKey(request, env);
    const type = (request.headers.get('content-type') ?? '').split(';')[0].trim().toLowerCase();
    if (!AUDIO_TYPES.includes(type)) throw new BadRequest(`Send audio as one of: ${AUDIO_TYPES.join(', ')}.`);
    const audio = await readBytes(request, MAX_AUDIO_BYTES);
    if (audio.length === 0) throw new BadRequest('The recording is empty.');
    const { text, language } = await transcribe(env, audio);
    if (!text) throw new HttpError(422, 'No speech recognised');
    let triage: Triage | null = null;
    try {
      triage = await runTriage(env, { type: 'Unknown', note: text.slice(0, 1000), lang: 'pl', nearby: [] });
    } catch {
      triage = null; // the transcript is still useful on its own
    }
    return json({ text, language, triage });
  }

  return json({ error: 'Not found' }, 404, c);
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    try {
      return await handle(request, env);
    } catch (e) {
      if (e instanceof BadRequest) return json({ error: e.message }, 400, cors(request, env));
      if (e instanceof HttpError) return json({ error: e.message }, e.status, cors(request, env));
      // Model or binding failure: never echo internals.
      return json({ error: 'AI is unavailable right now' }, 502, cors(request, env));
    }
  }
} satisfies ExportedHandler<Env>;
