# System overview — as built

## 1. Product boundary
CreatorFlow uses Douyin (China) for discovery and TikTok Vietnam for distribution and commerce. Source content is an inspiration/research input; the output must be a distinct, localized Vietnamese production, not a copied video.

Intended loop: discover → select → analyze → localize → produce → review → publish → measure → learn.

The repository includes a provider-backed LLM runtime for text analysis, 2–3-variant Vietnamese creative generation, evidence-grounded style synthesis and discovery query planning. SQL-backed project/trend/Style Lab foundations, tenant identity/isolation foundations, creator-shared link inbox, workspace-managed AI/discovery provider credentials, on-demand Brave public-web search and an approval-gated TikTok Research API adapter exist. Discovery connector results are not yet persisted into the candidate model or executed by recurring topic schedules; feedback learning is not yet an activated SQL-to-ranking loop. Trained ML, Douyin connectors, speech/music understanding and publishing remain integration work. The initial local video renderer is FFmpeg-only; After Effects is a future adapter, not a required current dependency.

## 2. Logical components

| Component | Responsibility | As-built status |
|---|---|---|
| Creator Web workspace | Creator-facing topic, trend, experiment and profile workflows | Blazor pages exist for trends, Style Lab and style profiles |
| API | Authenticated /api/v1 application contract | OIDC bearer + active tenant membership in SaaS mode; fixed Development-only API-key bootstrap for local pilot |
| Domain | Lifecycle invariants and deterministic style analysis | Implemented for projects, discovery, experiments, profiles and evidence |
| Infrastructure | SQL Server persistence through EF Core | DbContext mappings and SQL scripts exist |
| AI service | LLM-backed source analysis, Vietnamese variant generation and evidence-grounded style synthesis | Python FastAPI service calls a configured OpenAI-compatible chat-completions provider; outputs are schema-validated drafts |
| Discovery connectors | AI-assisted topic query planning plus permitted public/official search adapters | Brave indexed public-web search and approval-gated TikTok Research API are implemented as on-demand connectors; results are not yet persisted or scheduled |
| Vietnam trend connectors | TikTok Vietnam creator/video/sound ingestion | Not implemented; observations are manual or connector-supplied |
| Media engine | Source asset intake, FFmpeg MP4 encode, preview/download | Initial local Windows FFmpeg-only pipeline; advanced effects, STT/TTS, music analysis and hardware acceptance remain open |
| Publishing and analytics | TikTok publishing, product attachment and metric sync | Not implemented |
| Background processing | Durable video render jobs, retries and render status | SQL-backed video queue/worker implemented; discovery/provider orchestration, outbox and dead-letter handling remain open |

## 3. Runtime and trust boundaries
- Blazor Web calls the API server-side. The API key must not be exposed to browser storage or shipped to the client.
- API requests use validated OIDC bearer identity and active tenant membership in SaaS mode. The shared API key is a fixed Development-only bootstrap path, not a customer identity.
- SQL Server uses ConnectionStrings__CreatorFlow. Missing database configuration must fail with service-unavailable behavior rather than fabricated data.
- Video source/output files live outside the web root, are keyed by opaque IDs and checked for size/container signature; FFprobe validates source duration and video stream before rendering. Production malware scanning, rights registry and object-storage lifecycle remain open.
- Provider/model output is a draft. Creator approval is required for scripts and final publication.
- Only assets with documented rights/permission may enter production. No unauthorized scraping, watermark removal or platform-policy evasion.

## 4. AI runtime capabilities

The Python AI service has an OpenAI-compatible chat-completions adapter configured only through server-side environment variables. It exposes source analysis, structured 2–3-variant generation and evidence-grounded learning synthesis. Service API-key authentication is required; missing configuration fails closed. Provider responses are validated against Pydantic contracts, hallucinated experiment citations are rejected, and all outputs require creator review. The service does not silently substitute mock output. Authenticated .NET API proxy endpoints now forward analysis and variant-generation requests server-to-server. Blazor Style Lab now invokes source analysis and 2–3-variant generation through the .NET proxy. Style Profiles invokes LLM synthesis over evaluated experiments imported into the selected profile. AI-generated variants are persisted as ordinary Style Lab experiments after creator submission; an LLM synthesis draft can be submitted to Learning Memory, which persists reviewable proposals and immutable approved profile versions. The deterministic learning-analysis route remains global rather than profile-evidence-scoped.

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

The deterministic Style Learning endpoint analyzes evaluated experiment variants and returns repeated factors and topic breakdowns; it remains global rather than profile-scoped. A separate Learning Memory workflow persists evidence snapshots, proposals and immutable profile versions after human review. The endpoint's global experiment-pool limitation remains a multi-tenant blocker.

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
Apply numbered migrations through `database/Apply-Migrations.ps1`; current manifest includes 001–010 and 014–028, with 011–013 enabled only after explicit legacy-workspace ownership review. Migrations 017–020 cover video jobs, same-tenant relationships and workspace-scoped uniqueness; 024–028 add workspace AI providers and discovery schedules/credentials.

Do not apply these scripts to an unrelated production database. See ../GETTING_STARTED.md for runtime configuration.
