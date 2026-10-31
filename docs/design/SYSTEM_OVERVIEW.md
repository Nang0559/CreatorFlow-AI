# System overview — as built

## 1. Product boundary
CreatorFlow uses Douyin (China) for discovery and TikTok Vietnam for distribution and commerce. Source content is an inspiration/research input; the output must be a distinct, localized Vietnamese production, not a copied video.

Intended loop: discover → select → analyze → localize → produce → review → publish → measure → learn.

The repository now includes an actual provider-backed LLM runtime for text analysis, 2–3-variant Vietnamese creative generation and evidence-grounded style synthesis. The SQL-backed data foundation, metadata-driven discovery intake, Vietnam trend observations, Style Lab, creator style profiles and deterministic statistical learning preview also exist. Trained ML, persistence/invocation of LLM results from the creator workflow, external platform automation and media production remain integration work.

## 2. Logical components

| Component | Responsibility | As-built status |
|---|---|---|
| Creator Web workspace | Creator-facing topic, trend, experiment and profile workflows | Blazor pages exist for trends, Style Lab and style profiles |
| API | Authenticated /api/v1 application contract | Minimal API endpoints; API-key bootstrap authentication |
| Domain | Lifecycle invariants and deterministic style analysis | Implemented for projects, discovery, experiments, profiles and evidence |
| Infrastructure | SQL Server persistence through EF Core | DbContext mappings and SQL scripts exist |
| AI service | LLM-backed source analysis, Vietnamese variant generation and evidence-grounded style synthesis | Python FastAPI service calls a configured OpenAI-compatible chat-completions provider; outputs are schema-validated drafts |
| Discovery connectors | Douyin account/feed/video ingestion | Not implemented; endpoints accept metadata |
| Vietnam trend connectors | TikTok Vietnam creator/video/sound ingestion | Not implemented; observations are manual or connector-supplied |
| Media engine | Download/ingest, transcode, subtitle, voice and render | Not implemented |
| Publishing and analytics | TikTok publishing, product attachment and metric sync | Not implemented |
| Background processing | Scheduled polling, queues, retries and reconciliation | Not implemented |

## 3. Runtime and trust boundaries
- Blazor Web calls the API server-side. The API key must not be exposed to browser storage or shipped to the client.
- /api/v1/* is protected by X-CreatorFlow-Api-Key. This is service-level access, not per-user identity or tenant isolation.
- SQL Server uses ConnectionStrings__CreatorFlow. Missing database configuration must fail with service-unavailable behavior rather than fabricated data.
- Media files, when implemented, must live outside the web root and pass MIME, size, duration and file-signature validation.
- Provider/model output is a draft. Creator approval is required for scripts and final publication.
- Only assets with documented rights/permission may enter production. No unauthorized scraping, watermark removal or platform-policy evasion.

## 4. AI runtime capabilities

The Python AI service has an OpenAI-compatible chat-completions adapter configured only through server-side environment variables. It exposes source analysis, structured 2–3-variant generation and evidence-grounded learning synthesis. Service API-key authentication is required; missing configuration fails closed. Provider responses are validated against Pydantic contracts, hallucinated experiment citations are rejected, and all outputs require creator review. The service does not silently substitute mock output. Authenticated .NET API proxy endpoints now forward analysis and variant-generation requests server-to-server. Blazor Style Lab now invokes source analysis and 2–3-variant generation through the .NET proxy. Style Profiles invokes LLM synthesis over evaluated experiments imported into the selected profile. AI-generated variants are persisted as ordinary Style Lab experiments after creator submission; synthesis proposals are not yet persisted as proposal/version entities.

This LLM runtime is not a trained ML model. Persistent ML training, model registry, prediction and retraining require sufficient profile-scoped outcome data and are not yet implemented.

## 5. Implemented workflows

### 4.1 Discovery metadata
Creators can maintain topics, followed Douyin source-channel records and discovered video candidates. Candidates preserve discovery method (FollowedChannel, TopicExplore, Manual) and are deduplicated by Douyin video ID. These are metadata/workflow records, not proof Douyin has been queried or a channel actually followed on-platform.

### 4.2 Vietnam market observations
Trend signals distinguish creator channels, videos and sounds, and record platform, external ID, source URL, topic, attribution and point-in-time metrics. New observations are stored as timestamped snapshots. No automatic trend or music feed connector is implied.

### 4.3 Content projects and style experiments
A style experiment may link to a content project and has exactly two or three independently authored variants. Each variant passes Draft → Ready → Published → Measured. The experiment passes Draft → Running → Evaluated after every variant is measured and a creator confirms the winner.

### 4.4 Style profile and learning preview
A style profile stores positioning, editable Style Summary and evidence imported from confirmed experiment winners. A unique profile/experiment pair prevents duplicate evidence imports.

The Style Learning endpoint analyzes evaluated experiment variants and returns repeated factors and topic breakdowns. It is read-only; it does not persist an approval record or automatically alter profile guidance. The current endpoint verifies that the requested profile exists, but analyzes the evaluated experiment pool rather than a profile-filtered evidence set. This is a known limitation.

## 6. Data and workflow principles
1. Preserve source provenance: URLs, platform identifiers and discovery method.
2. Separate metadata from media: a URL/candidate is not a downloaded asset.
3. Preserve counterexamples: losing variants remain in their original experiments.
4. Require complete comparisons: do not evaluate before every variant has a measurement snapshot.
5. Do not overlearn: one win is evidence, not a permanent style rule.
6. Keep creator control: style proposals are advisory until reviewed and saved.
7. Do not claim causality from observed associations.
8. Deduplicate platform identities and evidence imports; future jobs/publishing need durable idempotency keys.

## 7. Deployment and schema
Apply numbered SQL scripts in order to a dedicated CreatorFlow database:
- 001_initial.sql
- 002_content_projects.sql
- 003_douyin_discovery.sql
- 004_video_topic_provenance.sql
- 005_vietnam_trends_and_style_lab.sql
- 006_creator_style_profiles.sql
- 007_style_learning_topic.sql

Do not apply these scripts to an unrelated production database. See ../GETTING_STARTED.md for runtime configuration.
