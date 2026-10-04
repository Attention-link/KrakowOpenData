# Kompas Krakowa AI Worker (Cloudflare Workers AI)

A small Cloudflare Worker that gives the Kompas Krakowa API three optional AI helpers. Every answer is a
**suggestion**: the API and the apps work exactly the same when this Worker is not deployed or fails.

| Route | Who calls it | What it does |
|---|---|---|
| `GET /ai/health` | anyone | `{ok, models, cache}` |
| `POST /ai/triage` | the API (`X-Ai-Key`) | category, severity 1–3, a short Polish summary without personal data, abuse / personal-data flags, likely duplicate |
| `POST /ai/transcribe` | the API (`X-Ai-Key`) | audio (webm/ogg/mp3/mp4/wav, ≤ 1 MB ≈ 60 s) → Whisper → `{text, language, triage}`; audio is never stored |
| `POST /ai/explain` | the browser (same origin), 30/min per IP (a venue shares one IP) | plain-language explanation of a score built only from the facts sent (≤ 8 KB body) |

Routing: the zone route `opendata.al.mt/ai/*` (zone `al.mt`) sends only `/ai/...` here. **"/ai/" never matches
"/api/"**, so the .NET API behind the tunnel is untouched. On `workers.dev` the same paths work with or without `/ai`
(e.g. `https://krakow-ai.<account>.workers.dev/triage`), so `Ai__BaseUrl` may point at either.

## Guardrails

- Triage: the resident's text is treated as data, never as instructions; the type must be one of the 12 report
  type NAMES; severity is clamped to 1–3; the summary is capped at 120 characters; a duplicate must be one of the
  nearby ids that were sent; a missing `containsPersonalData` flag counts as `true`. The API checks all of it again.
- Explain: only the given facts; a null value is "brak danych"; never claims a place or route is accessible or safe
  when data is missing; resident reports are kept apart as "zgłoszenia mieszkańców, niezweryfikowane"; at most 4 short
  sentences; ends with "Źródło: …, stan na <data>".
- JSON mode (`response_format: json_schema`) for triage, then validation and coercion; model failure → `502 {error}`.
- `X-Ai-Key` is compared in constant time; CORS only for `https://opendata.al.mt`.

## Cost: staying inside the free tier (10,000 neurons a day)

Approximate, from Cloudflare's published per-model prices (check the
[pricing page](https://developers.cloudflare.com/workers-ai/platform/pricing/) before relying on them):

| Call | Model | Typical size | ≈ neurons |
|---|---|---|---|
| triage | `@cf/meta/llama-3.1-8b-instruct-fast` (4,119 / M input, 34,868 / M output) | ~700 in, ≤ 200 out | **≤ 10** |
| explain | same 8B model | ~600 in, ≤ 300 out | **≈ 13** |
| explain with `MODEL_EXPLAIN` = `@cf/meta/llama-3.3-70b-instruct-fp8-fast` | 26,668 / M in, 204,805 / M out | ~600 in, ≤ 300 out | **≈ 75** |
| transcribe | `@cf/openai/whisper-large-v3-turbo` (≈ 47 / audio minute) + one triage | 60 s | **≈ 57** |

So a day of free tier is roughly 1,000 triaged reports, or ~175 one-minute voice notes, or a mix. Output is capped
(200 tokens triage, 300 explain), identical requests are answered from KV for 24 h when `CACHE` is bound, and the API
limits voice notes to 10 per device an hour and 300 a day.

## Deploy (manual; nothing deploys automatically)

```sh
cd workers/ai
npm install
npm run check && npm test                 # tsc --noEmit + node --test
npx wrangler login
npx wrangler kv namespace create CACHE    # optional: paste the id into wrangler.toml and uncomment [[kv_namespaces]]
npx wrangler secret put AI_KEY            # a long random string, e.g. `openssl rand -hex 32`
npx wrangler deploy
curl https://opendata.al.mt/ai/health
```

Then set on the API (Dockhand stack variables): `AI_BASE_URL=https://opendata.al.mt/ai` (or the workers.dev URL) and
`AI_KEY=<the same secret>`. The Workers AI binding needs the account to have Workers AI enabled (free plan is fine).

## AI use disclosure

Category, severity and summary suggestions and voice transcripts are produced by open models on Cloudflare Workers AI
(Llama 3.1 8B, Whisper). They are labelled "Sugestia AI · niezweryfikowane" and never change a score, a report's type
or its verification: a person decides. Audio is processed in memory and discarded; only the transcript is kept, as
the report's note (max 200 characters), exactly like a typed note.
