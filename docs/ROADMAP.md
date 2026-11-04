# Roadmap

## Phase 0 — Foundation
- [x] Architecture boundaries and repository conventions.
- [x] As-built system, data model, Style Lab/Style Learning and implementation-gap design documentation.
- [x] CI build and .NET/Python tests on GitHub-hosted runners (see PR validation; SQL Server migration execution is a separate outstanding gate).

## Phase 1 — Secure SaaS foundation (BLOCKING external-customer launch)
- [ ] Upgrade .NET 9 baseline to .NET 10 LTS before public launch.
- [x] Tenant and membership domain model plus SQL identity migration 011.
- [x] Configurable OIDC JWT bearer validation hook, active issuer+subject membership resolver and default tenant authorization gate.
- [x] EF Core tenant query filters, TenantId stamping/immutability and SQL migration 012 for all current business tables.
- [ ] Configure a production OIDC provider; implement MFA, invitations and membership administration.
- [x] Add workspace-scoped unique indexes and composite same-tenant FKs for the currently mapped core business relationships (migrations 018–020).
- [ ] Complete role-specific endpoint policies, SQL Server RLS and cross-tenant negative tests.
- [ ] Execute migration 012 on empty and populated SQL Server databases; add migration upgrade/rollback evidence.
- [ ] Audit trail, quotas, rate limits, billing and subscription lifecycle.
- [x] Content project persistence and migrations.
- [ ] Asset registry, rights/provenance evidence, retention and deletion.
- [ ] Private object storage adapter, signed URLs, malware/file validation and integration tests.

## Phase 2 — AI intelligence and editorial workflow
- [x] Configurable OpenAI-compatible LLM adapter and structured AI service contracts (source analysis, variant generation, evidence synthesis).
- [ ] Prompt/version registry, provider health checks and API/Web orchestration.
- [ ] Trend adapters using permitted APIs/data sources.
- [ ] Script generation, translation, scoring and human review UI.
- [x] Deterministic, explainable style learning across measured winning and losing variants, with topic-level analysis.
- [x] Persisted proposal review/approval and approved-profile versioning (human review required; production OIDC acceptance remains open).
- [x] LLM-assisted style explanation service contract and profile-scoped synthesis draft UI; approval remains a separate human action.
- [ ] Persisted AI outputs, approved profile-version prompt integration and evaluation harness.
- [ ] Trained ML outcome predictor, model registry, scheduled retraining, drift checks and champion/challenger rollout.
- [ ] Evaluation datasets and regression tests.

## Trend Selection Intelligence — foundation and next gates
- [x] Persist creator-authored Select/Reject/Watch decisions with candidate snapshots.
- [x] Expose authenticated feedback create/history endpoints.
- [x] Persist downstream outcome observations with source, timestamp and metrics snapshot.
- [x] Enforce append-only feedback and outcome history in SQL Server.
- [x] Add domain validation and API integration test coverage for feedback/outcome workflows.
- [ ] Add SQL Server migration integration tests for empty database and schema 009 upgrade paths.
- [x] Implement versioned candidate feature extraction with reproducible feature schema v1 (metadata-only; decision/status excluded).
- [ ] Complete end-to-end preference learning from tenant-scoped SQL feedback through LLM interpretation, reviewed rules and measurable next-ranking changes; keep Watch out of binary labels.
- [x] Initial on-demand Brave public-web and approval-gated TikTok Research API discovery adapters return validated links/metadata; full TikTok Vietnam scan coverage and persisted candidate ingestion remain open.
- [ ] Implement similarity and 24h/7d/30d saturation assessments.
- [ ] Implement rights evidence registry and separate content/recommendation/account-enforcement risk assessments.
- [x] Implement SQL-backed video render jobs, progress states, bounded retries and preview/download.
- [ ] Connect recurring topic schedules to provider execution; persist candidates; implement durable connector orchestration, outbox, dead-letter handling and realtime dashboard.
- [ ] Connect official publishing/analytics APIs with explicit final creator approval.

## Phase 3 — Hybrid execution and media production
- [ ] Private worker protocol (outbound mTLS/VPN), job leases and revocation.
- [ ] Durable queue/outbox, idempotent workers, bounded retries and dead-letter handling.
- [x] Initial FFmpeg-only pipeline creates vertical MP4 variants from a source asset; Z240 hardware acceptance is pending.
- [ ] STT, subtitle alignment, TTS, advanced effects/B-roll and music beat analysis.
- [x] Initial render validation, preview and MP4 download.
- [ ] Artifact version lineage, visual QA automation and production storage lifecycle.

## Phase 4 — TikTok + Douyin publishing and analytics
- [ ] Separate TikTok and Douyin developer apps, OAuth adapters and per-tenant encrypted connections.
- [ ] Provider capability/scope matrix, token refresh/revocation and contract tests.
- [ ] Official publishing, scheduling, idempotency and reconciliation.
- [ ] Performance analytics, comparable observation windows and attribution.

## Phase 5 — Production hardening and controlled SaaS launch
- [ ] Migration 010 SQL Server execution tests on empty and schema-009 databases.
- [ ] Security review, tenant isolation penetration tests and dependency/secret scanning.
- [ ] Load tests, backup/restore drills, monitoring, alerting and incident response.
- [ ] Billing, quotas, customer support, privacy/export/deletion and legal review.
- [ ] Closed pilot, per-tenant feature flags, rollback and public launch sign-off.

## Deployment profile

The selected target is SaaS + hybrid infrastructure + both TikTok Vietnam and Douyin China. See `docs/SAAS_HYBRID_ARCHITECTURE.md`, `docs/DEPLOYMENT_RUNBOOK.md` and `docs/PUBLIC_LAUNCH_CHECKLIST.md`. These are target designs; they do not imply the unchecked runtime components are implemented.
