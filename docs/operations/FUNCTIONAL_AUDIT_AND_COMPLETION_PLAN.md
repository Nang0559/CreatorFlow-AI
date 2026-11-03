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
| Projects | `Projects.razor` lists and creates projects only in the inspected page. No edit/open/archive control was observed in that page. | Partial; verify API/domain capabilities and intended lifecycle. |
| Style profiles | `StyleProfiles.razor` exists and uses the API client. Full workflow and persistence have not yet been verified. | Not yet assessed. |
| Video workflow | The inspected Web UI exposes asset selection/upload and render jobs with preview/download for completed jobs. The visible failure text still mentions After Effects and FFmpeg, which conflicts with the selected FFmpeg-only initial engine. | Partial; align UI/config/docs and verify actual worker execution. |
| Settings | `Settings.razor` checks the API root endpoint and labels the result “Đã kết nối”. This proves only the API root responded, not SQL, AI, storage, or render-worker readiness. | Failing health semantics; must report component-level checks honestly. |
| Web startup | `Program.cs` registers Interactive Server components and the API client. This alone does not prove every event handler or endpoint contract works. | Source configuration present; runtime verification required. |
| API startup | `CreatorFlow.Api/Program.cs` configures authentication/tenant middleware and maps some project/topic/video endpoints. Full endpoint-to-UI coverage remains to be inventoried. | Not yet assessed. |

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
