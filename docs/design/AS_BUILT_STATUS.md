# As-built status and implementation gaps

This is a release-truth checklist. A persisted entity, endpoint or UI control is not considered an external integration.

## Implemented in the repository

| Capability | Evidence in repository | Status |
|---|---|---|
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
- Multi-user identity, role/tenant model, per-profile ownership and audit actor.
- Persisted style proposal approval/rejection and profile version history.
- Profile-scoped deterministic style learning analysis; current deterministic analysis reads the global evaluated experiment pool. LLM synthesis is scoped to imported profile evidence.
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
