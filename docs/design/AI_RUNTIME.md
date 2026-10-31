# AI Runtime — as-built design

## 1. Purpose and boundary

The Python service provides actual LLM-backed capabilities behind a provider adapter. It is a separate service from the .NET API and currently exposes HTTP contracts; it is not yet invoked by the Blazor Style Lab or backed by SQL persistence.

The current milestone implements:
- source-text/transcript analysis for audience intent, topic, narrative mechanisms, hooks and Vietnam-localization opportunities;
- generation of exactly two or three differentiated Vietnamese short-video variants;
- evidence-grounded synthesis of creator-style learning, including conflicts, uncertainty and next experiment suggestions.

The service does not claim video understanding from pixels/audio. The current analysis input is text/transcript supplied by a caller. STT, vision, audio fingerprinting and media ingestion are future adapters.

## 2. Runtime topology

```text
Creator Web -> authenticated .NET API proxy (implemented; Blazor controls pending)
                 |
                 | X-CreatorFlow-AI-Key
                 v
        Python FastAPI AI service
                 |
                 | OpenAI-compatible /chat/completions
                 | Authorization: Bearer <server secret>
                 v
       Configured LLM provider and model
```

The AI service is not exposed directly to browser code. The .NET API forwards authenticated requests using a separate service credential; both the service-to-service credential and provider key remain server-side.

## 3. Configuration

| Environment variable | Required | Meaning |
|---|---:|---|
| `CREATORFLOW_AI_API_KEY` | Yes for /v1 routes | Internal service credential; minimum 32 characters |
| `CREATORFLOW_LLM_BASE_URL` | No | OpenAI-compatible API base; defaults to OpenAI API |
| `CREATORFLOW_LLM_API_KEY` | Yes for model calls | Provider secret |
| `CREATORFLOW_LLM_MODEL` | Yes for model calls | Exact model identifier |
| `CREATORFLOW_LLM_TIMEOUT_SECONDS` | No | Request timeout; default 60 |
| `CREATORFLOW_LLM_MAX_OUTPUT_TOKENS` | No | Output budget; default 3000, bounded 256–16000 |

Only HTTPS provider URLs are accepted, except localhost for development. Never commit credentials or put them in prompts/logs. The provider adapter uses the chat-completions JSON-object response mode.

## 4. HTTP contracts

- `GET /health`: liveness only; public.
- `GET /ready`: reports whether model configuration is present; it does not probe the remote provider.
- `POST /v1/analyze`: accepts source text, output language, topic and creator positioning. Returns structured analysis.
- `POST /v1/style-lab/generate-variants`: accepts source analysis, topic, creator positioning, approved style guidance, optional product facts, variant count (2–3) and target duration. Returns script, hook, creative angle, speaking/edit/audio approach, shot plan and CTA for each variant.
- `POST /v1/learning/synthesize`: accepts creator positioning and structured evidence rows. Returns proposed style summary, recurring strengths, conflicting evidence, next experiments, cited experiment IDs and uncertainty.

Every /v1 request requires `X-CreatorFlow-AI-Key`. The service rejects an unconfigured/short service key with 503 and a mismatched key with 401. Missing model credentials/configuration returns 503. Provider failures and malformed/contract-invalid model output return 502. No deterministic or mock response is substituted at runtime.

## 5. Output and originality safeguards

- Pydantic response models validate required fields, length bounds and exact requested variant count.
- Variant names must be unique.
- Learning synthesis may cite only experiment IDs supplied in the request.
- System prompts treat source/evidence payloads as untrusted data, not instructions.
- Prompts ask for abstract creative mechanisms, not copied dialogue, creator identity, signature phrases or exact shot sequences.
- Product facts must not be embellished into unsupported claims.
- Every response is marked `requires_human_review=true`; the service cannot publish content.

These are output-contract and prompt safeguards, not a guarantee that model output is always correct. Human review and evaluation datasets remain required.

## 6. Test strategy

The Python tests inject a fake LLM adapter at the dependency boundary. They verify service authentication, missing configuration readiness, provider invocation, structured generation, requested variant count and rejection of fabricated evidence citations. CI installs the Python package and runs `pytest ai/tests`.

The fake adapter is test-only. Production requests instantiate the real OpenAI-compatible HTTP adapter from environment configuration.

## 7. What is not implemented

- No trained ML model, feature store, model registry, model artifact persistence, retraining schedule, drift detection or champion/challenger rollout.
- No prompt registry/version persistence or provider cost/latency telemetry.
- No SQL persistence of AI analysis, generated scripts or learning proposals.
- The .NET API proxy is implemented, but no Blazor UI orchestration, SQL persistence or creator-facing approval workflow is wired yet.
- No source-video ingestion, STT, visual scene understanding, audio/music analysis, translation alignment, TTS or FFmpeg rendering.
- No Douyin/TikTok live connector, automatic publishing or metric synchronization.

## 8. ML learning design gate

Do not train a performance predictor from the current one-shot variant metrics without first making measurement windows comparable and profile/account scope trustworthy. Before ML training, add immutable metric observations, feature provenance, profile/tenant ownership, outcome definitions and a minimum sample policy. Train/validate against chronological holdouts; compare against a simple baseline; record dataset and feature versions, model artifact hash, metrics and training time; reject a model that does not beat the baseline; monitor drift and support rollback.

LLM synthesis and ML prediction have separate responsibilities:
- LLM: interpret source material, create original scripts and explain structured evidence.
- ML: estimate outcome distributions from historical experiments and support variant selection/next-test allocation.
- Deterministic rules: enforce lifecycle, rights, quality gates and minimum evidence requirements.

No model should autonomously publish or bypass creator approval.
