# As-built status and implementation gaps

This is a release-truth checklist. A persisted entity, endpoint or UI control is not considered an external integration.

## Implemented in the repository

| Capability | Evidence in repository | Status |
|---|---|---|
| SaaS tenant identity foundation | Tenant and TenantMembership domain entities, EF Core mappings, migration 011 and domain tests | Schema/model foundation only; OIDC and tenant authorization not active |
| Content project foundation | Domain, API, EF mapping and SQL | Implemented foundation |
| Topic management | Topic domain/API/UI and SQL | Metadata workflow |
| Douyin source channel records | Channel domain/API/UI and SQL | Metadata workflow; no live Douyin follow |
| Douyin video candidates | Candidate domain/API/UI and SQL | Manual/metadata intake; no automatic feed |
| Vietnam trend signals | Trend domain/API/UI and SQL | Manual/connector-supplied observations |
| Trend metric history | Snapshot entity, API and SQL | Timestamped history |
| 2–3 variant Style Lab | Domain lifecycle, API, UI, SQL and tests | Implemented |
| Manual metrics and winner confirmation | Metrics API and evaluation endpoint | Implemented |
| Immutable Style Lab metric history | Migration 008, EF mapping, capture on every measure, history API and domain tests | Implemented foundation for ML datasets; no predictor yet |
| Creator style profile | Profile API, UI, domain and SQL | Implemented |
| Winner evidence import | Learn endpoint, evidence snapshot and unique profile/experiment index | Implemented with duplicate protection |
| Topic-aware style learning | Deterministic engine, API preview, UI preview and unit tests | Implemented statistical baseline; not ML |
| LLM runtime | Provider adapter, structured contracts, authenticated FastAPI endpoints, tests, .NET proxy, Style Lab generation UI and profile-scoped learning synthesis UI | Implemented first LLM workflow; provider credentials required; proposal persistence/versioning pending |
| Human edit before guidance save | Draft-to-editor workflow | Manual review; no persisted approval/version |

## Not implemented / do not represent as complete
- Douyin login, automatic channel follow, feed polling and new-video monitor.
- TikTok Vietnam trend/channel/video/sound/music connector.
- Automatic TikTok metrics synchronization after publication.
- Media download/ingest and rights/license registry.
- Source video scene, transcript, voice, sound and story analysis pipeline.
- Automatic source-video transcription, visual/audio analysis and media ingestion pipeline.
- FFmpeg render, subtitle alignment and TTS pipeline.
- Product catalog, product matching, cart attachment and order attribution.
- TikTok OAuth, official publishing, scheduling and post reconciliation.
- Durable jobs, outbox/inbox, retries and connector observability.
- OIDC multi-user identity and tenant authorization; tenant foundation tables exist, but per-profile ownership and verified human audit actor are not yet enabled.

- Profile-scoped deterministic style learning analysis; current deterministic analysis reads the global evaluated experiment pool.
- Rich trend features: schema v1 currently covers intake metadata and title shape only; no video/audio/transcript/similarity/velocity extraction. LLM synthesis is scoped to imported profile evidence.
- Trained ML prediction, durable model registry/retraining and KPI/retention-aware optimization.
- Persisted LLM proposals, prompt registry/version history and immutable approved profile versions.
- Production evaluation datasets and end-to-end connector tests.

## Acceptance gates before production
1. Identity: enforce authenticated user and tenant ownership on profiles, experiments, projects and assets.
2. Source rights: record rights/permission and provenance before media processing.
3. Connectors: approved platform interfaces, rate limits, retries, health reporting and integration tests.
4. Media: validate file signature, MIME, duration, size and codec; preserve source/render lineage.
5. Publishing: explicit final human approval, official API authorization, idempotent post creation and reconciliation.
6. Analytics: record metric source and observation time, use comparable windows, distinguish engagement from commerce attribution.
7. AI/ML: profile-scoped evidence, persisted prompt/model versions, evaluation datasets, ML train/validation gates, model registry and drift monitoring.
8. Style learning: persisted proposal review, immutable approved versions, sample policy and regression tests.
9. Operations: secrets manager, backups, monitoring, alerting, durable queues and restore drills.


## Learning Memory update
Migration 009 and EF mappings now persist `StyleLearningProposals` and immutable `CreatorStyleProfileVersions`. The API exposes proposal list/create, approve/reject, and version-history routes. Approval updates current guidance and inserts a version; rejection requires a reason. The proposal-create route snapshots the profile's already-imported evidence and requires at least two distinct experiments. Review endpoints now derive the reviewer from the authenticated `sub`/NameIdentifier claim and ignore caller-supplied reviewer IDs; the shared API-key scheme is explicitly rejected as a human reviewer. Because OIDC is not yet configured, approve/reject currently fail closed for all existing API-key callers. Multi-creator/tenant deployment remains blocked until OIDC and tenant authorization are implemented. The AI synthesis endpoint still returns a draft; its caller must submit that draft to Learning Memory.


## Trend Selection Intelligence (migration 010)
Creator-authored Select/Reject/Watch feedback is persisted as append-only history with reason code, optional empty note and candidate metadata snapshot. Authenticated API supports feedback capture/history and downstream outcome observation write/history. Domain tests cover decision validation and JSON snapshots; API integration tests cover authentication, feedback round-trip, outcome round-trip, invalid numeric enums and missing candidates. Migration 010 independently guards index creation and installs SQL triggers that reject UPDATE/DELETE on both history tables. Feature snapshot now contains the versioned metadata baseline (`creatorflow.trend-selection`, schema version 1). Rich content, engagement, similarity and saturation features remain outstanding. SQL Server migration execution against empty/schema-009 databases is still awaiting a dedicated integration test. TikTok connector, similarity extraction, saturation scoring, rights registry, calibrated risk model and durable worker remain outstanding. No live TikTok feed is claimed.


## SaaS tenant identity foundation (migration 011)
Implemented: `Tenant` and `TenantMembership` domain entities, EF Core mappings, unique tenant slug and tenant+issuer+subject membership constraints, role/status validation, and domain tests. SQL migration `database/schema/011_tenant_identity_foundation.sql` creates the two identity tables. Deliberately not enabled: no API route creates tenants or memberships, no OIDC authentication is configured, and no existing business table is yet tenant-scoped. This is a safe foundation only; do not onboard external tenants or expose current business endpoints as multi-tenant until the next gates pass.
