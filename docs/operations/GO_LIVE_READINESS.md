# CreatorFlow-AI – Go-live readiness gates

This document prevents a green build or a healthy local URL from being mistaken for production readiness. See the [2026-11-02 repository audit report](RELEASE_AUDIT_2026-11-02.md) for the changes made and the remaining acceptance work.

## Implemented in the current branch
- Responsive mobile/desktop workspace with five primary areas.
- Tenant-scoped Share Inbox with HTTPS host validation, deduplication, state transitions and API routes.
- iPhone share-sheet handoff through a user-created Apple Shortcut; pasted-link fallback remains available.
- Discovery ranking and query-plan AI baseline from the previous implementation.
- Feedback evidence aggregation primitives and schema.
- Style Lab experiment tracking, metric capture and winner selection.
- Tenant-scoped video source library with rights confirmation, file-size/extension/signature checks and SHA-256 metadata.
- Durable SQL-backed video render queue: the current Windows worker uses FFmpeg/FFprobe to create H.264/AAC MP4; authenticated preview/download endpoints support byte ranges. After Effects is not required by the current renderer.
- The Assets UI can submit 2–3 independent style variants and displays job status, preview and MP4 download.
- Authenticated editing-package download remains available for each style variant.
- HP Z240 setup script checks tenant migration prerequisites and detects optional editors.

## Not production-complete yet – do not claim these are connected
1. **Live trend providers:** Brave public-web search and an approval-gated TikTok Research API adapter now support on-demand topic search and return source links/metadata. They do not provide complete TikTok coverage, continuous channel/video/sound metrics or Douyin feeds. TikTok Research API approval and real Z240 credentials remain external prerequisites.
2. **Share-link resolution:** links are durably received and deduplicated, but the current Share Inbox does not resolve short links or automatically identify a creator/channel/video and feed the discovery ranker. Do not imply that one shared URL immediately creates a followed channel.
3. **Discovery orchestration:** topic query planning and on-demand provider invocation exist. Recurring scheduled provider execution, candidate persistence, durable retries, source-health dashboard and ranking refresh still need end-to-end implementation.
4. **Feedback learning loop:** existing feedback and aggregation primitives are not yet a fully activated SQL → LLM interpretation → reviewed rule → next-ranking pipeline. Do not claim the system has learned until that full path is proven.
5. **Media ingest and understanding:** no production media download/permission workflow, video scene understanding, speech transcription, audio/music analysis or asset lifecycle has been acceptance-tested end to end.
6. **Editing/rendering:** FFmpeg-only rendering is implemented in code, but codec compatibility, audio sync, long renders, retries and performance are not yet accepted on the Z240. It is not a native CapCut project and does not yet provide advanced B-roll, speech transcription, TTS, beat-synced music or a professional effects library.
7. **Publishing/account integration:** no platform publishing or account OAuth is enabled. Preserve phone-only Douyin login and read-only/no-interaction policy.
8. **Production identity and operations:** production OIDC, HTTPS, backup/restore, observability, retention, per-route rate-limit tuning, proxy-aware client partitioning, incident handling and secret rotation must be configured and tested before public deployment. The API currently has a pre-authentication 600-request/minute IP limiter followed by a 300-request/minute authenticated subject/workspace limiter with separate API/video buckets, plus an opt-in CORS allowlist. This is not a complete production edge/WAF policy.

## Required acceptance evidence before Go-live
- [ ] HP Z240 SQL migrations 001–029 applied with verified checksums; tenant migrations 011–013 present, later tenant migrations 016–029 applied, and all required tenant/composite FKs trusted and workspace-scoped unique indexes verified. Legacy Discovery tables created by 014–015 remain excluded from multi-tenant runtime until migration 029 is applied and verified.
- [ ] Full .NET and Python test suites pass on the exact release commit.
- [ ] Docker images build on the exact release commit; containers stay healthy after reboot.
- [ ] Web/API/AI health, readiness, API authorization and tenant isolation verified against SQL.
- [ ] Discovery 014/015 tenant migration 029 applied and verified; all composite candidate/source/run/feedback foreign keys are trusted, and negative cross-tenant endpoint tests pass.
- [ ] Fresh local Keycloak volume imports the checked-in `creatorflow` realm; `scripts/Test-KeycloakRealmContract.ps1 -CheckLive` passes and a signed access token issued by that Keycloak instance is accepted by the API with the expected issuer/audience and active SQL membership. CI currently tests the API JwtBearer + membership path with a separately signed test JWT and tests Keycloak token issuance/audience independently; the combined live-Keycloak-to-API path remains open.
- [ ] Data Protection key ring path, Windows identity/DPAPI scope, ACLs and backup/restore are verified; multi-instance deployments use a shared key ring with a suitable shared at-rest protection strategy.
- [ ] iPhone Shortcut receives a Douyin share and the Share Inbox stores it once; duplicate submission is idempotent.
- [ ] Share links are not fetched server-side and unsupported URLs are rejected.
- [ ] FFmpeg worker exports a valid MP4 on the HP Z240; verify preview/download and audio/video integrity.
- [ ] Actual exported video passes dimensions, codec, duration, audio sync, caption safe-area, playback and human visual review for all 2–3 selected styles.
- [ ] Render worker retry/timeout/restart behavior is verified, and failed/abandoned jobs are recoverable without duplicate output.
- [ ] At least one provider is contract/policy-reviewed and a real discovery run produces persisted candidates with source attribution, coverage and refresh limits. Current on-demand Brave/TikTok Research results are not yet persisted.
- [ ] Feedback is durably stored, interpreted, reviewed, activated in scoped ranking rules and demonstrably changes later ranking; evaluation uses held-out/time-split evidence.
- [ ] Backup restore and rollback are rehearsed; monitoring and incident response are available.
- [ ] Security review confirms no Douyin credentials/cookies/session are collected and no platform interactions occur.

## Release policy
A build passing CI is necessary but not sufficient. Until every applicable gate has evidence on the target environment, label the deployment **Local Pilot / Pre-Go-live**, not Production.


## CI and SQL Server QC topology (2026-11-04)

- `.github/workflows/ci.yml` runs on GitHub-hosted runners: .NET restore/build/test, Python tests and PowerShell parser validation. It does not connect to the Z240 SQL Server.
- `.github/workflows/sqlserver-selfhosted-qc.yml` is a separate push/workflow-dispatch workflow on the dedicated `[self-hosted, Windows, X64]` runner. It runs the local QC database guard, fail-closed migration-state preflight, `database/Apply-Migrations.ps1` for migrations 001–029, `database/Verify-Database.ps1 -RequireTenantMigrations`, SQL connectivity smoke test and .NET tests. It refuses automatic legacy-workspace backfill when an existing ledger lacks tenant migrations 011–013; an operator must review and initialize that state explicitly.
- The SQL workflow deliberately has no `pull_request` trigger so untrusted PR code cannot execute on the machine holding the local QC database. A pending run is not a passing run; record its completed conclusion and job logs before claiming SQL acceptance.
- The verifier checks migration checksums for its selected manifest through 029 and verifies tenant ownership for mapped tenant tables. The legacy Discovery schema from 014–015 is not tenant-scoped; table existence alone is not evidence that it is safe for multi-tenant queries. Do not add those legacy tables to active multi-tenant runtime paths without a tenant-scoped schema and negative isolation tests.


## JWT + membership test coverage (2026-11-04)

The API test factory now exercises the real JwtBearer authentication handler with signed test tokens, followed by tenant selection and issuer+subject membership lookup. The suite verifies active membership success, Editor role enforcement from the membership row, denial when the token selects a workspace without membership, and denial for suspended membership. The backing provider in these tests is EF Core InMemory; SQL Server provider parity and the full live Keycloak token → API → SQL membership path are still required acceptance work.
