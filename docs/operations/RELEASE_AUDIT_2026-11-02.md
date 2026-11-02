# Repository audit — 2026-11-02

**Branch:** `feature/trend-selection-safety-intelligence`  
**Verdict:** Not Go-live ready. The current code supports a local pilot after the remaining build, SQL and HP Z240 acceptance checks pass. Do not treat a successful Web health check as product acceptance.

## Changes made during this audit

### Video rendering and asset handling
- Corrected the After Effects JSX payload injection: the serialized JSON object is now embedded as a JavaScript object rather than passed to `JSON.parse` as an object.
- Separated the After Effects sequence filename pattern (`frame_[#####].png`) from FFmpeg's input pattern (`frame_%05d.png`).
- Added FFprobe source validation with a 30-second process timeout and a bounded source duration of 1–600 seconds.
- Preserved source-video duration during FFmpeg encoding rather than allowing a shorter audio stream to truncate the video.
- Added tenant-specific source and output directories and path checks in the API and worker.
- Added atomic SQL batch creation for two or three distinct render styles; the UI now submits the variants as one transaction.
- Added upload request/form limits aligned with the 500 MiB file contract, temporary-file cleanup on failed writes, correct content types by extension, same-tenant SHA-256 duplicate handling and an asset detail endpoint.
- Extended upload cleanup to hashing failures and validated the stored filename against the SQL column limit.
- Added API tests for invalid batch size and unsupported render styles.

### Tenant and SQL integrity
- Added migration 018 for same-tenant video-asset/render-job and share-inbox/channel relationships.
- Added migration 019 for same-tenant foreign keys across mapped project, experiment, style, feedback and trend relationships.
- Added migration 020 for tenant-scoped uniqueness of source channels, video candidates, creator profiles and Vietnam trend signals.
- Aligned EF Core delete behavior and unique indexes with those SQL constraints.
- Strengthened database verification to compare composite FK table/column order, trusted/disabled state, delete action, and exact unique-index key order/filter definition.
- Aligned EF Core relationship metadata with the same-tenant composite foreign keys and SQL constraint names, rather than leaving the model on single-column relationships.
- Preserved the EF-owned connection lifetime in video API endpoints; only commands/readers are disposed per operation.
- Corrected the SQL Server QC workflow to use the default `self-hosted`, `Windows`, `X64` labels verified on the `tikt-ui` runner; actual SQL job pickup and SQL execution remain to be evidenced.
- Corrected the migration runner so non-tenant baseline setup does not attempt migrations 016–020 before tenant migrations 011–013 exist. Existing complete tenant schemas are detected and the later migrations are included automatically.

### Discovery ranking and documentation
- Hardened the heuristic ranker against non-finite values and made invalid learned adjustments neutral rather than a maximum penalty.
- Added Python numeric-safety tests.
- Updated README, API, data model, architecture, workflow, deployment, setup, SQL and Go-live documentation to distinguish implemented behavior from planned capabilities.

## Current implementation boundaries

| Area | Current state | Remaining work |
|---|---|---|
| Discovery | Heuristic ranker and LLM query planner exist | No permitted live provider/collector, no scheduled discovery orchestration |
| Share Inbox | Tenant-scoped link storage and deduplication | No short-link resolution, channel/video identification or ranking integration |
| Feedback learning | Creator feedback and metadata feature snapshots exist; aggregation primitives exist | No end-to-end SQL → LLM interpretation → reviewed rule → next-ranking loop |
| Style Lab | 2–3 variant experiments, metrics and persisted Learning Memory proposals/versions | Deterministic learning analysis is not scoped to imported profile evidence |
| Video rendering | SQL jobs, After Effects PNG render, FFmpeg MP4, preview/download | Real HP Z240 render and visual quality acceptance are still required; advanced effects, B-roll, STT/TTS and beat-synced music are not implemented |
| Commerce | Metrics can record clicks/orders in experiments | No product catalog matching, cart attachment or order attribution integration |
| Platform accounts | Phone-only Douyin safety policy; no account session collection | No approved OAuth/provider connection or publishing integration |
| SaaS security | OIDC and tenant foundations; composite FKs for mapped core entities | Production OIDC configuration, complete role policy, SQL RLS and cross-tenant negative tests |
| Discovery SQL schema | Migrations 014–015 provide Discovery tables | Those tables are not yet tenant-scoped; do not activate them for multi-tenant runtime use |

## Acceptance still required

1. Run .NET restore/build/test and Python test suites on the exact branch head.
2. Parse and execute the changed PowerShell scripts on Windows PowerShell 7.
3. Apply migrations 014–020 on the tagged local QC database after confirming tenant migrations 011–013 are complete; verify all checksums and trusted constraints.
4. Test a fresh non-tenant baseline separately to confirm migrations 016–020 are correctly deferred.
5. On HP Z240, upload a licensed 15–30 second MP4/MOV, submit two or three styles, render all variants, and verify dimensions, H.264/AAC where source audio exists, duration, playback, audio sync, retry and duplicate-upload behavior.
6. Run cross-tenant negative tests for assets, render jobs, inbox links, channels, projects, experiments, profiles, feedback and all mapped relationships.
7. Keep external Go-live blocked until official/eligible trend providers, Discovery tenant ownership, feedback learning, rights evidence, identity configuration, backup/restore, monitoring and operational controls are accepted.

## Database application safety

The new migrations are numbered 018–020 and must remain immutable once applied. Use the guarded initializer. If tenant migrations 011–013 are not already present, review the single-workspace backfill assumption before explicitly confirming them:

```powershell
.\database\Initialize-CreatorFlowDatabase.ps1 -SqlInstance localhost -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment
.\database\Verify-Database.ps1 -SqlInstance localhost -RequireTenantMigrations
```

If 011–013 are already fully installed, the initializer detects that state and applies/verifies the later migrations without re-running the ownership confirmation. Never use this QC initializer against production/customer databases.

## Evidence status

This audit changed repository code, SQL and documentation, but did not execute the user's Windows SQL Server, After Effects installation or HP Z240 hardware. No claim is made that the new C# code compiles, the migrations execute successfully, or an MP4 has rendered until those checks return evidence.
