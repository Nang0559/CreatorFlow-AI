# CreatorFlow API

All `/api/v1/*` endpoints require authentication and an active tenant context. SaaS requests use an OIDC bearer token plus `X-CreatorFlow-Tenant` workspace selector; the API independently validates the issuer+subject membership. The API-key bootstrap (`X-CreatorFlow-Api-Key`) is Development-only and is not a customer identity. Configure `ConnectionStrings__CreatorFlow` for SQL Server. `/health` and `/` are public health/metadata endpoints.

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

Learning is deliberately explicit and idempotent per profile/experiment: only an evaluated experiment with a measured winning variant can be added, and the same experiment cannot be added twice to one profile. Each evidence row snapshots the winning hook, speaking style, edit pacing, audio approach and measured performance. The profile's written style summary remains creator-editable; evidence is not automatically promoted into permanent rules after one win. An LLM synthesis draft can be submitted to Learning Memory for explicit human approval and immutable versioning.

`ProfileKey` is an application-level key scoped within a tenant, not a foreign key to an authenticated user. OIDC issuer+subject membership and tenant filters are implemented as foundations; production provider configuration, full role policy and cross-tenant negative tests remain release gates.


## AI Style Learning

`GET /api/v1/style-profiles/{profileId}/learning-analysis?topic=Kitchen&minimumViewsPerVariant=100`

Returns a read-only analysis of evaluated experiments, including repeated winning factors, topic breakdown, sample eligibility and a proposed style summary. The profile must exist. `topic` is optional; `minimumViewsPerVariant` defaults to 100 and must be non-negative (API maximum: 1,000,000,000).

New style experiments may include an optional `topic` string (maximum 200 characters). Historical experiments retain a NULL topic until explicitly classified; no topic is inferred from their names.

The endpoint is advisory only. It does not mutate `CreatorStyleProfiles.StyleSummary` or create an approval decision.


### Style Learning implementation notes

New Style Lab create requests may include an optional `topic` (maximum 200 characters). The Style Profiles UI can request the read-only learning preview and place its suggested summary into the editable Style Summary draft; the creator must review and save it.

The current learning-analysis endpoint checks that the supplied profile ID exists but analyzes the global evaluated experiment pool rather than only evidence imported into that profile. Do not use this route as a profile-specific learning boundary until that scoping is fixed and tested. The analysis is deterministic and descriptive, not an LLM verdict; see [design/STYLE_LAB_AND_LEARNING.md](design/STYLE_LAB_AND_LEARNING.md).


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

All outputs are drafts with `requires_human_review=true`. These endpoints are real LLM calls only when a provider is configured. The Python service itself does not persist prompts, proposals or profile versions; the Blazor Style Lab invokes the .NET proxy, generated variants can be saved as experiments, and synthesis drafts can be submitted to Learning Memory. Trained ML artifacts are not implemented. See [design/AI_RUNTIME.md](design/AI_RUNTIME.md).


### .NET API proxy to the AI service

The authenticated .NET API exposes server-side proxy routes:
- `POST /api/v1/ai/analyze` with `{ "text": "...", "language": "vi", "topic": "...", "creatorPositioning": "..." }`.
- `POST /api/v1/ai/style-lab/generate-variants` with `sourceAnalysis`, `topic`, `creatorPositioning`, optional `styleSummary` / `productContext`, `variantCount` (2 or 3) and `targetDurationSeconds`.
- `POST /api/v1/style-profiles/{id}/ai-learning-synthesis` loads only evaluated experiments explicitly imported into that profile, sends winner/loser factors and winner metrics to the LLM, and returns a draft synthesis. At least two imported evaluated experiments and a non-empty profile positioning are required. The result is a draft; the creator may submit it to Learning Memory, which persists a proposal for explicit approval/rejection and creates an immutable profile version only after approval.

Configure `CREATORFLOW_AI_BASE_URL` and `CREATORFLOW_AI_API_KEY` in the API host. The proxy validates HTTPS (or localhost HTTP), requires a 32-character service key, and forwards provider responses/status without exposing the AI service key to the browser. Missing configuration and unavailable service return 503; timeout returns 504. The Python service separately requires its own provider credentials. The creator-facing Blazor controls are not yet wired to these proxy routes.


### Learning Memory
- `GET /api/v1/style-profiles/{id}/learning-proposals` lists persisted proposals for a profile.
- `POST /api/v1/style-profiles/{id}/learning-proposals` stores a reviewed-ready draft using the profile's imported evidence snapshot. Body: `{ "proposedSummary": "...", "algorithmVersion": "llm-synthesis-v1" }`. At least two imported evaluated experiments are required.
- `POST /api/v1/style-profiles/{id}/learning-proposals/{proposalId}/approve` approves a pending proposal, updates current guidance and creates the next immutable profile version. Body: `{ "reviewerId": "...", "reason": "..." }`.
- `POST /api/v1/style-profiles/{id}/learning-proposals/{proposalId}/reject` rejects a pending proposal; a reason is required.
- `GET /api/v1/style-profiles/{id}/versions` returns immutable profile versions in descending version order.

Review identity is derived from authenticated `sub`/NameIdentifier claims; caller-supplied reviewer IDs are ignored, and the shared API-key scheme is rejected as a human reviewer. Production OIDC configuration and role-policy acceptance remain required. Only Approved proposals create versions; AI generation itself does not activate a profile.

Apply the repository migration manifest through `database/Apply-Migrations.ps1`; migrations 008 and 009 are required for metric history and Learning Memory. Migrations 011–013 require explicit legacy-workspace ownership confirmation; migrations 018–020 add same-tenant relationship and workspace-scoped uniqueness safeguards.


## Trend Selection Intelligence
- POST /api/v1/douyin/videos/{id}/feedback with { "decision": "Select", "reasonCode": "Hook", "reasonNote": "" }.
- GET /api/v1/douyin/videos/{id}/feedback returns immutable history.
- Decisions are Select, Reject and Watch; Watch is not a negative training label.
- Candidate metadata and versioned metadata-only feature snapshot schema v1 are persisted; this is not a trained preference model.
- Migration: database/schema/010_trend_selection_feedback.sql.
- TikTok scanning and calibrated account-risk prediction are not implemented by this migration.


## Trend Selection feedback and outcome observations

- `POST /api/v1/douyin/videos/{id}/feedback` with `decision` (`Select`, `Reject`, `Watch`), `reasonCode` and optional `reasonNote`. An empty note is valid.
- `GET /api/v1/douyin/videos/{id}/feedback` returns append-only decision history.
- `POST /api/v1/douyin/videos/{id}/feedback/{feedbackId}/outcomes` with `outcomeKind` (`Adapted`, `Published`, `MarketPerformance`, `CreatorAssessment`), optional `outcomeValue`, object-valued `metricsSnapshotJson` and `source`.
- `GET /api/v1/douyin/videos/{id}/feedback/{feedbackId}/outcomes` returns append-only downstream outcome observations.

Feedback and outcome routes require authenticated tenant context and validate that the feedback belongs to the candidate in the route. SQL triggers reject UPDATE and DELETE for both history tables. `Watch` remains an uncertain/deferred decision and must not be converted to a negative preference label. Feature snapshots use versioned metadata-only schema v1. The Development-only API key is a bootstrap service credential, not an individual creator identity.


## Share Inbox

- `POST /api/v1/share-inbox` with `{ "sharedUrl": "https://v.douyin.com/..." }`.
- `GET /api/v1/share-inbox` lists links in the current workspace.
- Links are HTTPS-validated, host-allowlisted, deduplicated per tenant and stored as user-submitted metadata. The server does not resolve short links, fetch pages, scrape content or automatically follow a channel. A shared link is an optional priority signal, not a prerequisite for topic-based discovery.

## Video source assets and render jobs

- `POST /api/v1/video/assets`: multipart `file`, `confirmRights=true`, optional `rightsNote`. Accepts MP4/MOV/M4V up to 500 MB. Same-tenant duplicate SHA-256 uploads return the existing asset. User confirmation is not an automated legal-rights check.
- `GET /api/v1/video/assets` and `GET /api/v1/video/assets/{id}`: current-workspace assets only.
- `POST /api/v1/video/render-jobs`: create one render job with `assetId`, `title`, `hook`, optional `callToAction`, optional `styleKey`.
- `POST /api/v1/video/render-jobs/batch`: atomically create 2–3 distinct styles with `assetId`, `title`, `hook`, optional `callToAction`, `styleKeys`. The group is committed as one SQL transaction.
- `GET /api/v1/video/render-jobs` and `GET /api/v1/video/render-jobs/{id}`: status and render metadata.
- `GET /api/v1/video/render-jobs/{id}/preview` and `GET /api/v1/video/render-jobs/{id}/download`: authenticated MP4 streaming/download with HTTP Range support.

The Windows worker uses After Effects/aerender to produce a 1080×1920 PNG sequence and FFmpeg/FFprobe to validate and encode H.264/AAC MP4. It is a local pilot renderer; advanced effects, B-roll, speech transcription, TTS, music analysis and publishing are not part of this endpoint contract. See [video rendering runbook](operations/VIDEO_RENDERING_AE_FFMPEG.md).
