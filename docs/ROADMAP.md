# Roadmap

## Phase 0 — Foundation
- [x] Architecture boundaries and repository conventions.
- [x] As-built system, data model, Style Lab/Style Learning and implementation-gap design documentation.
- [x] CI build and .NET/Python tests on GitHub-hosted runners (see PR validation; SQL Server migration execution is a separate outstanding gate).

## Phase 1 — Secure SaaS foundation (BLOCKING external-customer launch)
- [ ] Upgrade .NET 9 baseline to .NET 10 LTS before public launch.
- [ ] OIDC authentication, MFA, authorization, tenant and membership model.
- [ ] Tenant-scoped repositories, EF filters, SQL constraints and cross-tenant negative tests.
- [ ] Audit trail, quotas, rate limits, billing and subscription lifecycle.
- [ ] Content project persistence and migrations.
- [ ] Asset registry, rights/provenance evidence, retention and deletion.
- [ ] Private object storage adapter, signed URLs, malware/file validation and integration tests.

## Phase 2 — AI intelligence and editorial workflow
- [x] Configurable OpenAI-compatible LLM adapter and structured AI service contracts (source analysis, variant generation, evidence synthesis).
- [ ] Prompt/version registry, provider health checks and API/Web orchestration.
- [ ] Trend adapters using permitted APIs/data sources.
- [ ] Script generation, translation, scoring and human review UI.
- [x] Deterministic, explainable style learning across measured winning and losing variants, with topic-level analysis.
- [x] Persisted proposal review/approval and immutable approved-profile versioning (review is fail-closed until verified OIDC identity is configured).
- [x] LLM-assisted style explanation service contract and Style Profiles synthesis UI (provider credentials required; proposal submission remains an explicit review step).
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
- [x] Implement versioned candidate metadata feature extraction (schema v1); rich media/semantic/market features remain outstanding.
- [ ] Implement preference learning; exclude Watch from binary labels and keep outcome labels separate.
- [ ] Implement compliant TikTok Vietnam scan adapters with explicit provider eligibility, source provenance and coverage.
- [ ] Implement similarity and 24h/7d/30d saturation assessments.
- [ ] Implement rights evidence registry and separate content/recommendation/account-enforcement risk assessments.
- [ ] Implement durable background jobs, progress reporting, retries, dead-letter handling and realtime dashboard.
- [ ] Connect official publishing/analytics APIs with explicit final creator approval.

## Phase 3 — Hybrid execution and media production
- [ ] Private worker protocol (outbound mTLS/VPN), job leases and revocation.
- [ ] Durable queue/outbox, idempotent workers, bounded retries and dead-letter handling.
- [ ] Deterministic FFmpeg pipeline, STT, subtitle alignment and TTS.
- [ ] Render validation, preview and artifact versioning.

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
