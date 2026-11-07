# CreatorFlow AI

AI-powered content intelligence and automated video production platform.

CreatorFlow AI turns a creator-approved idea or licensed source asset into a reviewed, localized, edited, and publish-ready video package. The platform is designed around explicit rights tracking, human approval gates, reproducible media jobs, and permitted integrations.

## Initial architecture

- **Web**: ASP.NET Core / Blazor creator workspace.
- **API**: versioned application API.
- **Application / Domain**: use cases, policies, and business rules.
- **Infrastructure**: SQL Server persistence, storage, queues, and provider adapters.
- **Workers**: background orchestration for discovery and media jobs.
- **AI services**: Python FastAPI service with a configurable OpenAI-compatible LLM runtime for source analysis, Vietnamese variant generation and evidence-grounded style synthesis. Trained ML and speech/media intelligence must be distinguished from LLM prompting and heuristic ranking.
- **Media engine**: FFmpeg-only for the initial local-pilot renderer, producing vertical H.264/AAC MP4 variants. The renderer is intended to remain provider-neutral so an official After Effects render adapter can be added later without changing business workflows. Target-machine rendering and output quality require acceptance.
- **Tests**: unit, integration, contract, SQL and end-to-end tests.

## Principles

1. Rights and provenance are first-class data. Users must confirm they have permission before upload; this confirmation is not an automated legal-rights verification.
2. No platform scraping, account-session extraction, login automation or publishing bypass is assumed. Integrations must use permitted sources and official APIs.
3. AI output is a draft. Human review is required before publishing.
4. Media jobs must be durable, observable, retry-safe and tenant-isolated. These properties require end-to-end acceptance, not just a queue table.
5. Secrets are supplied through environment variables or a secret manager, never committed.
6. UI controls must invoke real business actions. A visible button, callback, or HTTP 200 response alone is not evidence that a feature is complete.

## Getting started

See the [latest repository audit](docs/operations/RELEASE_AUDIT_2026-11-02.md), [whole-platform functional audit and completion plan](docs/operations/FUNCTIONAL_AUDIT_AND_COMPLETION_PLAN.md), [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [docs/ROADMAP.md](docs/ROADMAP.md). For the isolated Windows SQL Server test host and self-hosted runner, follow [docs/SQLSERVER_QC_RUNNER.md](docs/SQLSERVER_QC_RUNNER.md). The selected SaaS/hybrid deployment profile is documented in [docs/SAAS_HYBRID_ARCHITECTURE.md](docs/SAAS_HYBRID_ARCHITECTURE.md), [docs/DEPLOYMENT_GUIDE.md](docs/DEPLOYMENT_GUIDE.md), [docs/DEPLOYMENT_RUNBOOK.md](docs/DEPLOYMENT_RUNBOOK.md), and [docs/PUBLIC_LAUNCH_CHECKLIST.md](docs/PUBLIC_LAUNCH_CHECKLIST.md).

## Current status

The repository contains a foundation, an initial LLM runtime, discovery/ranking components, project and style workflows, and a video-render pipeline. It is not yet accepted as a complete end-to-end product.

Known limitations that must remain visible until implemented and tested:

- Discovery query planning is AI-assisted. On-demand Brave public-web search and approval-gated TikTok Research API connectors return validated source links; results are not yet persisted as candidates or executed by recurring schedules. Ranking remains heuristic and feedback aggregation is not yet proven as an activated end-to-end learning loop.
- Shared links are stored as user-provided inputs; automatic short-link resolution, permitted source enrichment and ranking-priority integration are not yet proven.
- Complete live trend ingestion, recurring connector execution, Douyin integration, speech/music analysis, advanced effects, product/cart attribution and official publishing are not complete.
- The initial renderer is FFmpeg-only; a completed job must still be validated as a real playable MP4 on the target machine.
- SQL migration execution, tenant-isolation negative tests, target-machine performance and full end-to-end acceptance remain release gates.

See [AI Runtime design](docs/design/AI_RUNTIME.md), [video rendering runbook](docs/operations/VIDEO_RENDERING_AE_FFMPEG.md), [go-live gates](docs/operations/GO_LIVE_READINESS.md), [as-built status](docs/design/AS_BUILT_STATUS.md) and the [functional audit plan](docs/operations/FUNCTIONAL_AUDIT_AND_COMPLETION_PLAN.md).
