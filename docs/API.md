# CreatorFlow API

All `/api/v1/*` endpoints require the `X-CreatorFlow-Api-Key` header. Configure `Authentication__ApiKey` with at least 32 characters and `ConnectionStrings__CreatorFlow` for SQL Server. `/health` and `/` are public health/metadata endpoints.

## Content projects

- `GET /api/v1/content-projects?skip=0&take=20`
- `POST /api/v1/content-projects` with `{ "title": "..." }`

## Topic discovery

- `GET /api/v1/topics?skip=0&take=50`
- `POST /api/v1/topics` with `{ "name": "Home gadgets", "keywords": "kitchen, home organization" }`
- `PUT /api/v1/topics/{id}` with `{ "name": "...", "keywords": "..." }`
- `POST /api/v1/topics/{id}/deactivate`

## Douyin source channels

- `GET /api/v1/douyin/channels?followed=true`
- `POST /api/v1/douyin/channels` with `{ "douyinSecUserId": "...", "displayName": "...", "profileUrl": "https://www.douyin.com/user/..." }`
- `POST /api/v1/douyin/channels/{id}/follow`
- `POST /api/v1/douyin/channels/{id}/unfollow`

## Douyin video candidates

- `GET /api/v1/douyin/videos?status=Candidate&discoveryMethod=TopicExplore`
- `POST /api/v1/douyin/videos` with Douyin video ID, HTTPS source URL, optional `sourceChannelId`, optional `contentTopicId` and `discoveryMethod` (`FollowedChannel`, `TopicExplore`, `Manual`).
- `POST /api/v1/douyin/videos/{id}/select`
- `POST /api/v1/douyin/videos/{id}/reject`
- `POST /api/v1/douyin/videos/{id}/queue`

List endpoints support `skip` and `take` (1..100). Duplicate Douyin channel/video identifiers return HTTP 409. Invalid state transitions return HTTP 409.

## Important integration boundary

These endpoints persist creator-entered or connector-supplied metadata. They do not themselves browse Douyin, authenticate to Douyin, follow accounts on Douyin, fetch a creator feed, or download video files. A future connector must be separately implemented and validated; the current API must not imply those operations have happened.

## Vietnam market trend intelligence

Trend kinds: `CreatorChannel`, `Video`, `Sound`. Platforms: `TikTokVietnam`, `YouTubeShortsVietnam`, `FacebookReelsVietnam`, `Other`.

- `GET /api/v1/vietnam-trends?kind=Sound&platform=TikTokVietnam`
- `POST /api/v1/vietnam-trends` with kind, platform, externalId, sourceUrl (HTTPS), creator/title/topic, optional soundName/soundArtist, publishedAt and the observed views/likes/comments/shares/saves.
- `POST /api/v1/vietnam-trends/{id}/observe` with a newer metrics snapshot.

Every create and observe operation stores an immutable timestamped metric snapshot; query history with `GET /api/v1/vietnam-trends/{id}/history`. The schema/API do not imply automatic access to platform trend feeds or music charts; records must be entered manually or supplied by a separately validated connector.

## Style Lab experiments

Create an experiment linked optionally to a content project with exactly 2 or 3 variants:

- `POST /api/v1/style-experiments` with `name`, a testable `hypothesis`, optional `contentProjectId`, and `variants[]`. Each variant needs `name`, `hook`, `speakingStyle`, `editPacing`, `audioApproach` and `script`.
- `GET /api/v1/style-experiments?status=Running`
- `POST /api/v1/style-experiments/{id}/variants/{variantId}/ready`
- `POST /api/v1/style-experiments/{id}/start`
- `POST /api/v1/style-experiments/{id}/variants/{variantId}/publish`
- `POST /api/v1/style-experiments/{id}/variants/{variantId}/measure` with views, likes, comments, shares, saves, followsGained, productClicks, orders, averageWatchSeconds and videoDurationSeconds. Each successful call appends an immutable timestamped observation.
- `GET /api/v1/style-experiments/{id}/observations` returns chronological metric observations across the experiment's variants, including derived engagement, follow, click, order and watch-retention rates.
- `POST /api/v1/style-experiments/{id}/evaluate` with `winningVariantId`.

An experiment cannot start until every variant is ready, and cannot be evaluated until every variant has been published and measured. Engagement rate is descriptive; the creator explicitly selects the winner after considering the stated hypothesis and the wider performance evidence.


## Creator style profiles

A profile is keyed by a stable creator/account key and keeps creator positioning, editable style guidance and evidence from evaluated experiments.

- `GET /api/v1/style-profiles?skip=0&take=50`
- `POST /api/v1/style-profiles` with `profileKey`, `displayName` and optional `positioning`.
- `GET /api/v1/style-profiles/{id}` returns guidance and its evidence history.
- `PUT /api/v1/style-profiles/{id}/guidance` updates `displayName`, `positioning` and `styleSummary`.
- `POST /api/v1/style-profiles/{id}/learn/{experimentId}` imports the confirmed winning variant from an evaluated experiment.

Learning is deliberately explicit and idempotent per profile/experiment: only an evaluated experiment with a measured winning variant can be added, and the same experiment cannot be added twice to one profile. Each evidence row snapshots the winning hook, speaking style, edit pacing, audio approach and measured performance. The profile's written style summary remains creator-editable; evidence is not automatically promoted into permanent rules after one win. A later AI synthesis step should identify recurring traits across multiple experiments and propose changes for creator approval.

`ProfileKey` is an application-level owner key, not an authentication identity. The current API-key authentication is a bootstrap service credential; multi-user authorization and ownership isolation must be implemented before exposing multiple creators' profiles in a shared deployment.


## AI Style Learning

`GET /api/v1/style-profiles/{profileId}/learning-analysis?topic=Kitchen&minimumViewsPerVariant=100`

Returns a read-only analysis of evaluated experiments, including repeated winning factors, topic breakdown, sample eligibility and a proposed style summary. The profile must exist. `topic` is optional; `minimumViewsPerVariant` defaults to 100 and must be non-negative (API maximum: 1,000,000,000).

New style experiments may include an optional `topic` string (maximum 200 characters). Historical experiments retain a NULL topic until explicitly classified; no topic is inferred from their names.

The endpoint is advisory only. It does not mutate `CreatorStyleProfiles.StyleSummary` or create an approval decision.


### Style Learning implementation notes

New Style Lab create requests may include an optional `topic` (maximum 200 characters). The Style Profiles UI can request the read-only learning preview and place its suggested summary into the editable Style Summary draft; the creator must review and save it.

The current learning-analysis endpoint checks that the supplied profile ID exists but analyzes the global evaluated experiment pool. It does not yet scope analysis to evidence imported into that profile or enforce tenant ownership. Do not use this route as a multi-creator data isolation boundary. The analysis is deterministic and descriptive, not an LLM verdict; see [design/STYLE_LAB_AND_LEARNING.md](design/STYLE_LAB_AND_LEARNING.md).


## Python AI runtime (LLM-backed)

The Python AI service exposes server-to-server endpoints. Every `/v1/*` endpoint requires `X-CreatorFlow-AI-Key`; configure `CREATORFLOW_AI_API_KEY` with at least 32 characters. The key must remain server-side.

Configure a real OpenAI-compatible chat-completions provider with:
- `CREATORFLOW_LLM_BASE_URL` (defaults to `https://api.openai.com/v1`; HTTPS required except localhost)
- `CREATORFLOW_LLM_API_KEY`
- `CREATORFLOW_LLM_MODEL`
- Optional `CREATORFLOW_LLM_TIMEOUT_SECONDS` and `CREATORFLOW_LLM_MAX_OUTPUT_TOKENS`

No mock or deterministic fallback is used in runtime. Missing provider credentials/configuration returns HTTP 503; provider failures or invalid structured output return HTTP 502. `GET /ready` reports whether provider configuration exists, not whether the remote provider is reachable. `GET /health` is a liveness check.

- `POST /v1/analyze`: analyze source transcript/text, audience intent, topic, narrative mechanisms and localization opportunities. Source text is reference material only; the response explicitly identifies elements not to copy.
- `POST /v1/style-lab/generate-variants`: generate exactly 2 or 3 distinct Vietnamese creative variants with hook, speaking style, edit pacing, audio approach, script, shot plan and CTA.
- `POST /v1/learning/synthesize`: synthesize repeated strengths, conflicting evidence and next experiments from supplied structured evidence. The service rejects experiment citations not present in the request.

All outputs are drafts with `requires_human_review=true`. These endpoints are real LLM calls only when a provider is configured. They do not yet persist prompts, learning proposals, profile versions, ML model artifacts or generated scripts in SQL, and they are not yet invoked by the Blazor/API Style Lab workflow. See [design/AI_RUNTIME.md](design/AI_RUNTIME.md).


### .NET API proxy to the AI service

The authenticated .NET API exposes server-side proxy routes:
- `POST /api/v1/ai/analyze` with `{ "text": "...", "language": "vi", "topic": "...", "creatorPositioning": "..." }`.
- `POST /api/v1/ai/style-lab/generate-variants` with `sourceAnalysis`, `topic`, `creatorPositioning`, optional `styleSummary` / `productContext`, `variantCount` (2 or 3) and `targetDurationSeconds`.
- `POST /api/v1/style-profiles/{id}/ai-learning-synthesis` loads only evaluated experiments explicitly imported into that profile, sends winner/loser factors and winner metrics to the LLM, and returns a draft synthesis. At least two imported evaluated experiments and a non-empty profile positioning are required. The result is not persisted; the creator may put it into the editable Style Summary and save manually.

Configure `CREATORFLOW_AI_BASE_URL` and `CREATORFLOW_AI_API_KEY` in the API host. The proxy validates HTTPS (or localhost HTTP), requires a 32-character service key, and forwards provider responses/status without exposing the AI service key to the browser. Missing configuration and unavailable service return 503; timeout returns 504. The Python service separately requires its own provider credentials. The creator-facing Blazor controls are not yet wired to these proxy routes.


### Learning Memory
- `GET /api/v1/style-profiles/{id}/learning-proposals` lists persisted proposals for a profile.
- `POST /api/v1/style-profiles/{id}/learning-proposals` stores a reviewed-ready draft using the profile's imported evidence snapshot. Body: `{ "proposedSummary": "...", "algorithmVersion": "llm-synthesis-v1" }`. At least two imported evaluated experiments are required.
- `POST /api/v1/style-profiles/{id}/learning-proposals/{proposalId}/approve` approves a pending proposal, updates current guidance and creates the next immutable profile version. Body: `{ "reviewerId": "...", "reason": "..." }`.
- `POST /api/v1/style-profiles/{id}/learning-proposals/{proposalId}/reject` rejects a pending proposal; a reason is required.
- `GET /api/v1/style-profiles/{id}/versions` returns immutable profile versions in descending version order.

Review identity is currently supplied by the API caller because the bootstrap API-key authentication has no individual user identity/role model. Do not treat `reviewerId` as verified identity or expose this workflow to multiple creators until user/tenant authorization is implemented. Only Approved proposals create versions; AI generation itself does not activate a profile.

Apply SQL scripts `database/schema/001_initial.sql` through `009_style_learning_memory.sql` in numeric order. Scripts 008 and 009 are required for metric history and Learning Memory respectively.
