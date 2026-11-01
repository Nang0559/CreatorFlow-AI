# Roadmap

## Phase 0 — Foundation
- [x] Architecture boundaries and repository conventions.
- [x] As-built system, data model, Style Lab/Style Learning and implementation-gap design documentation.
- [ ] Verify CI on GitHub-hosted runners.

## Phase 1 — Secure project and asset foundation
- [ ] Authentication, authorization, tenant model, audit trail.
- [ ] Content project persistence and migrations.
- [ ] Asset registry, provenance, license evidence, retention policy.
- [ ] Storage adapter and integration tests.

## Phase 2 — AI intelligence and editorial workflow
- [x] Configurable OpenAI-compatible LLM adapter and structured AI service contracts (source analysis, variant generation, evidence synthesis).\n- [ ] Prompt/version registry, provider health checks and API/Web orchestration.
- [ ] Trend adapters using permitted APIs/data sources.
- [ ] Script generation, translation, scoring, human review UI.
- [x] Deterministic, explainable style learning across measured winning and losing variants, with topic-level analysis.
- [ ] Persisted proposal review/approval and approved-profile versioning.
- [x] LLM-assisted style explanation service contract (standalone AI service; not yet wired to profile UI).\n- [ ] Persisted AI outputs, approved profile-version prompt integration and evaluation harness.\n- [ ] Trained ML outcome predictor, model registry, scheduled retraining, drift checks and champion/challenger rollout.
- [ ] Evaluation datasets and regression tests.

## Trend Selection Intelligence — foundation and next gates
- [x] Persist creator-authored Select/Reject/Watch decisions with candidate snapshots.
- [x] Expose authenticated feedback create/history endpoints.
- [x] Persist downstream outcome observations with source, timestamp and metrics snapshot.
- [x] Expose authenticated outcome create/history endpoints.
- [x] Enforce append-only feedback and outcome history in SQL Server.
- [x] Add domain validation and API integration test coverage for feedback/outcome workflows.
- [ ] Add SQL Server migration integration tests for empty database and schema 009 upgrade paths.
- [ ] Implement versioned candidate feature extraction with reproducible feature schema.
- [ ] Implement preference learning; exclude Watch from binary labels and keep outcome labels separate.
- [ ] Implement compliant TikTok Vietnam scan adapters with explicit provider eligibility, source provenance and coverage.
- [ ] Implement similarity and 24h/7d/30d saturation assessments.
- [ ] Implement rights evidence registry and separate content/recommendation/account-enforcement risk assessments.
- [ ] Implement durable background jobs, progress reporting, retries, dead-letter handling and realtime dashboard.
- [ ] Connect official publishing/analytics APIs with explicit final creator approval.

## Phase 3 — Media production
- [ ] Deterministic FFmpeg pipeline, STT, subtitle alignment, TTS.
- [ ] Render validation, preview, artifact versioning.

## Phase 4 — Publishing and analytics
- [ ] Official platform authorization and publishing integrations.
- [ ] Scheduling, idempotent publishing, reconciliation.
- [ ] Performance analytics and attribution.

## Phase 5 — Production hardening
- [ ] Queue/outbox, retries, rate limits, observability, alerting.
- [ ] Security review, load tests, backup/restore, deployment automation.
