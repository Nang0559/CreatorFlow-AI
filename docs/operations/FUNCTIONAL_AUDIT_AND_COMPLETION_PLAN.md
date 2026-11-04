# Functional Audit and Completion Plan

**Target branch:** `feature/trend-selection-safety-intelligence`  
**Audit started:** 2026-11-03  
**Rule:** A rendered page, an HTTP 200 response, or a wired UI callback is not by itself proof that a business capability works end to end.

## Acceptance contract

Every user-visible control must be classified as one of:
- **Implemented and verified** — real action, persisted/returned result, authorization and tenant boundaries checked, automated test evidence recorded.
- **Implemented but failing** — action exists, but runtime, contract, persistence, or UX is failing.
- **Partial** — only a subset of the intended business workflow exists.
- **Not implemented** — placeholder, dead control, missing endpoint/worker/provider, or no real business behavior.
- **Not applicable** — intentionally unavailable with a documented reason and an honest UI state.

No placeholder action may report success. Errors must be actionable and visible in Vietnamese. Busy states must prevent duplicate submissions without making controls permanently inert.

## Required end-to-end areas

1. Workspace overview and navigation.
2. Discovery: topics, source/provider ingestion, candidates, ranking, shared-link inbox, feedback and learning.
3. Studio: source analysis, idea/script generation, variant creation and review.
4. Video production: rights confirmation, asset upload/storage, render queue, FFmpeg processing, job status, preview and MP4 download.
5. Projects: create, list, open, update, archive and connect related work.
6. Style profiles: evidence aggregation, review, versioning and application to new variants.
7. Media library: upload, metadata, permissions, retrieval and deletion lifecycle.
8. Analytics: metric provenance, experiment comparison, outcome evaluation and feedback loop.
9. Settings and operations: API/AI/SQL/worker health, configuration validation and security status.
10. Cross-cutting: authentication, tenant isolation, authorization, antiforgery, validation, idempotency, observability, localization, accessibility and responsive behavior.

## Initial source inspection — 2026-11-03

This is a source-level baseline, not a completed audit or runtime acceptance.

| Area | Evidence observed | Initial classification |
|---|---|---|
| Trends page | `Trends.razor` wires LoadAsync, SaveSharedLinkAsync, LoadShareInboxAsync and CreateAsync. Its own UI text says shared links are stored but do not yet expand short links or change ranking. | Partial; runtime has reported HTTP 400/404 and must be traced to endpoint contracts. |
| Analytics | `Analytics.razor` reads trend observations and style experiments and computes simple aggregate totals. It explicitly describes totals as raw recorded data, not direct platform metrics. | Partial; no evidence yet of complete attribution or experiment evaluation. |
| Projects | `Projects.razor` and API currently expose list/create only; edit/open/archive lifecycle is not present in the inspected UI/API. Project page now validates title and shows busy/success/error feedback. | Partial; lifecycle operations remain to be designed and implemented with tenant-safe persistence. |
| Style Lab | The page supports AI analysis/generation, 2–3 variants, ready/published/measure/evaluate actions and editing-package download. The AI analysis/generation methods initially had no visible form controls; source input, positioning/style/product context and explicit AI action buttons are now exposed. An editing-package button rendered with no label; this has been corrected. Action feedback and basic required-field validation were added. | Partial; verify every route, metric validation, attribution and end-to-end AI/provider behavior. |
| Style profiles | `StyleProfiles.razor` exposes create, analyze, LLM synthesis, learn and guidance update flows. | Partial; verify API contracts, tenant scoping, review/version history and failure UX. |
| Video workflow | Upload, asset listing, render queue, batch jobs, job status, preview and download endpoints exist. Worker claims SQL jobs and invokes FFmpeg/FFprobe. | Partial; target-machine execution, retry correctness, output validation and worker health still require evidence. |
| Settings | `Settings.razor` checks only the API root endpoint. It now explicitly states that this does not verify SQL Server, AI or worker; environment label reads the actual Web host environment. | Partial; component-level readiness endpoint and operational diagnostics remain to implement. |
| Web startup | `Program.cs` registers Interactive Server components and the API client. This alone does not prove every event handler or endpoint contract works. | Source configuration present; runtime verification required. |
| API startup | `CreatorFlow.Api/Program.cs` configures authentication/tenant middleware and maps some project/topic/video endpoints. Full endpoint-to-UI coverage remains to be inventoried. | Not yet assessed. |

## Changes made during this audit

- `Trends.razor`: basic client validation for required source ID/title/HTTPS URL, busy state, success/error feedback and duplicate/validation messages.
- `Projects.razor`: required title validation, duplicate-submit guard, busy state and explicit success/error feedback.
- `StyleLab.razor`: fixed the blank editing-package button label; exposed the previously unreachable AI source-analysis and variant-generation methods; added basic required-field checks and visible action feedback.
- `Settings.razor`: corrected the health-check claim so API-root response is not represented as proof of SQL/AI/worker readiness; environment name is no longer hard-coded.
- CSS: added shared styles for required/optional labels, validation summaries, success notices and disabled controls.

These are source changes only until CI and Z240 runtime tests pass. No release gate is marked complete by these edits.

## Work sequence

### A. Inventory before broad changes
- Enumerate every routed page, component, form, button, link, callback and disabled condition.
- Trace every callback to application handler, API route, persistence, background worker/provider and returned UI state.
- Enumerate API routes and map each to UI callers and automated tests.
- Enumerate SQL migrations, tables, foreign keys, tenant keys, indexes and migration execution order.
- Enumerate workers, queues, media storage and external provider adapters.
- Identify dead, duplicate, placeholder, unreachable and undocumented paths.

### B. Repair by vertical business slice
For each slice, implement and test the full path: UI → API → application/domain → SQL/storage/queue → worker/provider → result → UI. Add failure, retry, duplicate-submit, authorization and cross-tenant tests.

### C. Release gates
- **G1 UI:** every control has a verified behavior or an explicitly unavailable state.
- **G2 API:** UI/API contracts, validation, auth and error semantics pass integration tests.
- **G3 Data:** migrations, constraints, tenant isolation and persistence tests pass on a disposable QC database.
- **G4 AI:** provider calls, provenance, ranking, feedback and learning behavior are evidenced; heuristic behavior is not described as trained ML.
- **G5 Media:** real source asset → queued job → FFmpeg output → valid MP4 → preview/download passes on the target machine.
- **G6 Go-live:** full end-to-end, security, backup/recovery, deployment and documentation acceptance passes.

A gate remains **not verified** until its test evidence is attached. No estimated completion percentages and no Go-live claim based only on static inspection.


## Followed-channel automatic discovery and media intake — expanded finding

The current channel endpoints only create/list/follow/unfollow curated channel records. The API source explicitly documents that actual feed synchronization is a separate connector. The current video intake endpoint accepts manually supplied metadata and deliberately does not download media. There is no deployed provider connector or channel polling worker, so automatic new-video discovery and automatic download are NOT implemented.

Added:
- `database/schema/021_channel_sync_and_video_download_queue.sql`: tenant-scoped channel sync state and rights-gated video download queue, with composite tenant foreign keys, unique candidate jobs, status/attempt constraints and due-queue indexes.
- `docs/design/CHANNEL_AUTOMATIC_DISCOVERY_AND_VIDEO_INTAKE.md`: intended workflow, safety requirements, provider contract, rights gate and acceptance tests.

Not yet implemented/verified:
- live permitted provider connector and metadata polling;
- scheduler/scan-run persistence and cursor continuation;
- AI ranking wired to discovered candidates;
- rights-review UI/API;
- actual media downloader and worker;
- asset-library linkage and end-to-end tests.

Migration 021 is storage groundwork only. Do not enable polling or claim automatic downloads until an approved connector and worker exist and pass target-environment tests.


## Channel management and follow scheduling implementation

- Added `/channels` page for registering a public channel identifier/profile URL, listing channels and following/unfollowing them, with required-field validation and visible errors/success messages.
- Added desktop and mobile navigation entries for channel management.
- Follow/unfollow API operations now transactionally create/re-enable or disable the corresponding `DiscoveryChannelSyncStates` row. The response distinguishes schedule enabled from actual scan execution.
- The schedule is intentionally inert until a permitted provider connector and discovery worker are deployed. The UI explicitly says no video has been scanned/downloaded yet.
- Commit evidence: `235b6424` (API follow schedule), `43f5f29b` (channel management page), `d4605bca` (navigation).

Remaining blocker: no approved Douyin/TikTok metadata connector or media-download capability has been provided/configured. The application must not scrape, extract sessions/cookies, or download media absent a documented permission basis. Automatic discovery/download remains not operational until a connector and worker are implemented and tested.


## SaaS identity, workspace and resource audit update (2026-11-03)

### Implemented in this branch
- Added `/account` UI for OIDC users: workspace list/switch, member list, invitations, Owner role changes and member suspension.
- Added CSRF-protected browser operations for workspace selection and logout.
- Added account/workspace navigation and explicit message when OIDC login is not configured.
- Added migration `022_tenant_subscriptions_and_usage.sql`: plan catalog, tenant subscriptions, idempotent usage events and tenant storage metadata.
- Added migration 022 to the tenant-enabled migration runner and Z240 read-only migration validation.
- Added design and operator documentation for SaaS account/resource boundaries.

### Confirmed gaps / not Go-live ready
- Login is not active until a real OIDC provider, callback URLs, API audience/scope and secrets are configured.
- Initial Owner bootstrap remains a privileged one-time API operation; it is not yet a guided onboarding screen.
- Invitation tokens are displayed once but are not delivered by email.
- Migration 022 is schema groundwork only: no payment-provider checkout/webhooks, commercial plan catalog/prices, recurring billing, quota reservation/enforcement, or complete AI/render/storage metering is wired.
- The media file store and all worker execution paths still need tenant-scoped authorization and cross-tenant tests.
- Existing role guard is a baseline restriction, not a full per-operation policy matrix. Analyst/Publisher and every resource endpoint require negative authorization tests.
- OIDC access-token refresh, central session revocation, verified MFA policy and account recovery are provider/deployment gates.

### Migration safety
- `Apply-Migrations.ps1` still intentionally excludes 011–013 and dependent 016–022 unless both `-IncludeTenantMigrations` and `-ConfirmLegacyWorkspaceAssignment` are explicitly supplied.
- Migration 012 assigns all listed existing rows to the single `legacy-workspace`. This is safe only after confirming the QC database is a single-workspace dataset. Never run it on multi-customer data without a reviewed ownership mapping.
- Do not ask the operator to run migrations until the Z240 validation report and the actual QC ownership/ledger state have been reviewed.

## Downloader technology and source integrity decision (2026-11-03)

The acquisition design is now explicit: a reviewed provider connector resolves a canonical provider ID to an authorized stream; .NET `HttpClient` performs bounded streaming to tenant-isolated temporary storage; SHA-256 and FFprobe verify the acquired source; an atomic move commits the immutable source master. FFmpeg is reserved for derivative rendering, not provider acquisition.

No generic yt-dlp fallback is approved for arbitrary Douyin/TikTok URLs. A downloader package does not establish permission to access or reuse media. No login/session extraction, access-control circumvention, DRM bypass, rate-limit evasion or watermark/attribution removal is permitted.

Source masters must remain byte-for-byte immutable with provenance and rights evidence. Derivatives are separate files with separate hashes and must preserve legally/contractually required attribution. A clean master is accepted only when supplied by the rights holder or an authorized source.

This is a documented engineering decision, **not an implemented downloader**. Migration 021 still supplies only queue/schedule storage. Connector, rights-review API/UI, acquisition worker, FFprobe/hash validation, asset linkage and end-to-end tests remain open.


## Update — November 4, 2026: on-demand topic search connector

The earlier findings above about missing automatic channel polling and media acquisition remain valid. A limited discovery-search capability has since been implemented and must not be confused with automatic followed-channel sync:

- AI query planning can now be followed by an authenticated .NET API call to the discovery search connector.
- Brave Search Web API searches indexed public pages and returns validated TikTok profile/video links where available.
- TikTok Research API can return public Vietnam video metadata only when the Research API project is approved and credentials are configured.
- Workspace Owner/Admin can manage connector keys at /settings; credentials are encrypted server-side and stored per workspace by migration 028. The browser receives presence flags only.
- The connector is on-demand. It does not persist returned results as discovery candidates, run from recurring topic schedules, follow accounts, download media, or interact with TikTok.

Therefore the original audit's statements that followed-channel polling and automatic media downloads are not implemented remain accurate. Update acceptance gates to test both the new on-demand search path and the still-open scheduled/persisted ingestion path separately. See docs/operations/DISCOVERY_CONNECTORS.md, docs/design/TREND_SELECTION_INTELLIGENCE.md and docs/design/AS_BUILT_STATUS.md.
