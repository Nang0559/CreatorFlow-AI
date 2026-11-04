# As-built status and implementation gaps

This is a release-truth checklist. A persisted entity, endpoint or UI control is not considered an external integration.

## Implemented in the repository

| Capability | Evidence in repository | Status |
|---|---|---|
| SaaS tenant identity and row-isolation foundation | Tenant/Membership/Invitation entities; API JWT validation; Web OIDC Authorization Code + PKCE; issuer+subject membership; one-time Owner bootstrap; invitation acceptance and membership administration; EF tenant filters; migrations 011–013 and composite tenant/unique-key migrations 018–020 | Baseline implemented in code; real provider configuration, email delivery, full role matrix, SQL execution and cross-tenant security tests remain pending |
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
| Video source library and rights confirmation | Tenant-scoped asset upload, SHA-256 provenance, size/format checks, SQL metadata | Implemented foundation; codec/duration validation occurs in render worker |
| Automated video render | Durable SQL jobs, After Effects JSX project generation, AE image-sequence render, FFmpeg H.264/AAC MP4, preview/download proxy | Implemented pipeline; hardware/visual acceptance on HP Z240 still required |
| LLM runtime | Provider adapter, structured contracts, authenticated FastAPI endpoints, tests, .NET proxy, Style Lab generation UI and profile-scoped learning synthesis UI | Implemented first LLM workflow; provider credentials required; synthesis output is a draft and must be submitted to Learning Memory for review |
| Human review and Learning Memory | Persisted proposal create/list/approve/reject, reviewer from authenticated identity, immutable profile versions | Implemented foundation; production OIDC and profile-scoped analysis remain release gates |

## Not implemented / do not represent as complete
- Douyin login, automatic channel follow, feed polling and new-video monitor.
- TikTok Vietnam trend/channel/video/sound/music connector.
- Automatic TikTok metrics synchronization after publication.
- Server-side media download/ingest from external platforms and a full rights/license evidence registry. User-uploaded source assets with an explicit rights confirmation are implemented.
- Source video scene, transcript, voice, sound and story analysis pipeline.
- Automatic source-video transcription, visual/audio analysis and media ingestion pipeline.
- Automatic subtitle transcription/alignment, TTS, advanced B-roll, music/beat analysis and professional effect/template library.
- Product catalog, product matching, cart attachment and order attribution.
- TikTok OAuth, official publishing, scheduling and post reconciliation.
- Durable discovery/provider connector jobs, outbox/inbox, retries and connector observability. Video render jobs are durable but do not constitute connector orchestration.
- Production OIDC provider configuration and contract tests; MFA enforcement is delegated to the provider.
- Invitation and membership administration APIs exist; invitation delivery is manual until SMTP/provider integration.
- Analyst writes are blocked centrally and Publisher writes are blocked outside future publishing routes; full endpoint-specific role policies remain incomplete.
- SQL Server Row-Level Security is not yet configured. Composite same-tenant foreign keys are added for the currently mapped business relationships by migrations 018–019; migration 020 changes channel/video/trend/profile uniqueness to workspace scope. Remaining unmapped Discovery SQL tables still need explicit tenant ownership before those tables can be used by a multi-tenant runtime.
- SQL Server execution of the current migration manifest through 020 has not been verified on the user's HP Z240 in this review. Migration 012's legacy-workspace backfill assumes all listed existing business rows belong to one workspace; review ownership before applying it.
- Background workers that access tenant-owned business data must scope each operation by TenantId; the video worker processes durable jobs globally but each job/output is tenant-keyed.

- Profile-scoped deterministic style learning analysis; current deterministic analysis reads the global evaluated experiment pool. LLM synthesis is scoped to imported profile evidence.
- Trained ML prediction, durable model registry/retraining and KPI/retention-aware optimization.
- Prompt registry/version history and production prompt-evaluation datasets. Learning proposals and immutable approved profile versions are implemented through Learning Memory.
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
Migration 009 and EF mappings now persist `StyleLearningProposals` and immutable `CreatorStyleProfileVersions`. The API exposes proposal list/create, approve/reject, and version-history routes. Approval updates current guidance and inserts a version; rejection requires a reason. The proposal-create route snapshots the profile's already-imported evidence and requires at least two distinct experiments. Review endpoints now derive the reviewer from the authenticated `sub`/NameIdentifier claim and ignore caller-supplied reviewer IDs; the shared API-key scheme is explicitly rejected as a human reviewer. Because production OIDC is not yet configured, approve/reject remain unavailable to API-key callers. Configure the provider and API audience before enabling interactive review. The AI synthesis endpoint still returns a draft; its caller must submit that draft to Learning Memory.


## Trend Selection Intelligence (migration 010)
Creator-authored Select/Reject/Watch feedback is persisted as append-only history with reason code, optional empty note and candidate metadata snapshot. Authenticated API supports feedback capture/history and downstream outcome observation write/history. Domain tests cover decision validation and JSON snapshots; API integration tests cover authentication, feedback round-trip, outcome round-trip, invalid numeric enums and missing candidates. Migration 010 independently guards index creation and installs SQL triggers that reject UPDATE/DELETE on both history tables. Versioned feature snapshot schema v1 is now extracted at creator review and persisted with feedback (discovery method, source/topic presence, title shape, publication age and discovery age); creator decision and mutable candidate status are excluded to prevent label leakage. The schema is deterministic and metadata-only; it is not a trained preference model. SQL Server migration execution against empty/schema-009 databases is still awaiting a dedicated integration test. TikTok connector, similarity extraction, saturation scoring, rights registry, calibrated risk model and durable discovery/provider workers remain outstanding. No live TikTok feed is claimed.


## SaaS tenant identity and data isolation (migrations 011–012)
Implemented in code: `Tenant` and `TenantMembership` domain entities; configurable OIDC JWT bearer validation; request tenant selection validated against exact issuer+subject membership; API default authorization requires a resolved active tenant; EF Core applies tenant filters to business entities and stamps/locks TenantId on writes. The local API-key path is restricted to a fixed legacy workspace in Development only. Migration 011 creates identity tables; migration 012 adds TenantId, tenant FK and index to all current business tables and backfills existing single-workspace data to `legacy-workspace`.

Not yet release-complete: production OIDC provider configuration, MFA enforcement, invitation delivery, endpoint-specific role policies, SQL Server execution tests for the current migration manifest, RLS, tenant ownership for unmapped Discovery SQL tables, and cross-tenant negative tests. Migration 012 must not be applied to a database containing multiple customer workspaces until a reviewed row-ownership mapping is prepared.


## Discovery connector milestone — migrations 025–028

The current branch `feature/keycloak-oidc-local` includes topic scheduler foundations (025–027) and workspace-scoped connector credentials (028). The Trends UI can request an AI-generated query plan and automatically invoke the authenticated discovery search endpoint. The endpoint supports Brave Search public-web indexing and an optional TikTok Research API adapter that is usable only after the relevant TikTok Research project approval.

Workspace connector keys are stored in `dbo.DiscoveryConnectorConfigurations` per tenant and encrypted using ASP.NET Core Data Protection. Settings endpoints expose credential-presence flags only and enforce Owner/Admin management. Environment variables remain a server-side fallback. Migration 028 is tenant-dependent and is included in the migration runner's tenant migration set.

**Not yet complete:** discovered results are not persisted into the durable discovery candidate model; recurring topic/calendar schedules do not yet run connector searches; no media download, channel follow, TikTok account interaction, rights clearance or publishing is performed by these connectors. Brave results may be incomplete/stale. TikTok Research API access remains approval-gated. End-to-end validation with a real Brave key and, separately, an approved TikTok Research project on the Z240 is still required.

### Release evidence boundary

At commit `e2524f3d570460b9fd9f1462d2cfea046b74efe4`, GitHub CI run `41823809975` completed successfully for .NET build/tests, Python tests and PowerShell script validation. This does not prove SQL Server migration 028 was applied to the HP Z240, credentials were configured, a provider returned live results, or the Z240 runtime passed acceptance. Record those as separate host acceptance evidence.
