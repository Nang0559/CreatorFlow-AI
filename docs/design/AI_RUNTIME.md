# AI Runtime — as-built design

## 1. Purpose and separation of responsibilities

CreatorFlow uses three distinct mechanisms:
- **LLM** interprets source text, creates original Vietnamese concepts/scripts and explains evidence.
- **Deterministic statistics** enforces transparent minimum-sample and repeated-pattern rules.
- **ML** will estimate likely outcomes from historical observations. A trained predictor is not yet implemented.

The Python service currently provides real provider-backed LLM analysis, 2–3 variant generation and evidence-grounded style synthesis. The .NET API proxies these calls; Blazor Style Lab and Style Profiles invoke them. LLM synthesis returns a review-required draft, not an autonomous profile update.

## 2. Runtime topology

```text
Blazor Web -> authenticated .NET API -> Python FastAPI -> OpenAI-compatible provider
                     |                     |
                     | SQL Server           | structured Pydantic contracts
                     v                     v
               creator experiments     model responses
               profiles/evidence       review-required drafts
```

Provider credentials and the internal service key stay server-side. The AI service is not called directly by browser code.

## 3. Configuration

| Environment variable | Required | Meaning |
|---|---:|---|
| `CREATORFLOW_AI_API_KEY` | Yes for /v1 routes | Internal service credential; minimum 32 characters |
| `CREATORFLOW_LLM_BASE_URL` | No | OpenAI-compatible API base; defaults to OpenAI API |
| `CREATORFLOW_LLM_API_KEY` | Yes for model calls | Provider secret |
| `CREATORFLOW_LLM_MODEL` | Yes for model calls | Exact model identifier |
| `CREATORFLOW_LLM_TIMEOUT_SECONDS` | No | Request timeout; default 60 |
| `CREATORFLOW_LLM_MAX_OUTPUT_TOKENS` | No | Output budget; default 3000, bounded 256–16000 |

Only HTTPS provider URLs are accepted, except localhost for development. Missing credentials fail closed; there is no runtime mock fallback.

## 4. Implemented workflows

- `POST /v1/analyze`: text/transcript analysis for audience intent, topic, narrative mechanisms, hooks and Vietnam-localization opportunities.
- `POST /v1/style-lab/generate-variants`: creates exactly 2 or 3 differentiated Vietnamese scripts, including hook, creative angle, speaking/edit/audio approach, shot plan and CTA.
- `POST /v1/learning/synthesize`: synthesizes supplied profile evidence into repeated strengths, conflicts, uncertainty and next experiment ideas.
- Authenticated .NET proxy routes expose source analysis and generation; profile synthesis loads evaluated experiments explicitly imported into that profile before calling the LLM.
- Generated variants can be reviewed and saved as normal experiments. Learning synthesis remains an ephemeral draft that a creator may copy into editable guidance.

The model must cite only supplied experiment IDs. All outputs require human review. Prompt instructions are safeguards, not proof of factual correctness or rights clearance.

## 5. New metric memory foundation

Migration `008_style_variant_metric_observations.sql` adds append-only, timestamped metric observations for Style Lab variants. Every successful `/measure` call stores a snapshot alongside the current summary metrics. A later observation is permitted while the experiment remains Running; each timestamp must be strictly later than the prior observation. The API exposes `GET /api/v1/style-experiments/{id}/observations`.

Snapshots preserve views, likes, comments, shares, saves, follows, product clicks, orders, average watch time and video duration. Derived engagement, follow, click, order and watch-retention rates are calculated from each snapshot. This is the foundation for comparable measurement windows and future ML dataset construction; it is not itself a trained predictor.

Apply database scripts 001 through 008 in order.

## 6. Test strategy

Python tests inject a fake LLM adapter only at the test boundary. .NET domain tests verify metric snapshot immutability, derived rates and increasing observation timestamps. CI runs Python tests and .NET build/test.

## 7. Not yet implemented

- Trained ML model, feature pipeline, artifact registry, persisted model, retraining, drift detection or champion/challenger rollout.
- Durable LLM proposal entities, approval/rejection workflow, prompt registry/version persistence and immutable approved profile versions.
- Comparable-window orchestration and source provenance for external metric sync.
- Video/audio understanding, STT, TTS, media rendering and automatic platform publishing.
- Multi-tenant user identity and profile ownership authorization.

## 8. ML acceptance gates

Do not train on cumulative metrics measured at arbitrary times. Build labels from comparable post-publication windows (for example 24h and 7d), preserve observation/source provenance, and prevent observations from the same experiment leaking across train/test splits. Use chronological holdouts, compare to a naive baseline, persist dataset/feature/model versions and reject models that do not beat baseline. Keep predictions advisory; creator approval remains mandatory.
