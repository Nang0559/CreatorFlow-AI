# CreatorFlow AI

AI-powered content intelligence and automated video production platform.

CreatorFlow AI turns a creator-approved idea or licensed source asset into a reviewed, localized, edited, and publish-ready video package. The platform is designed around explicit rights tracking, human approval gates, reproducible media jobs, and official publishing integrations.

## Initial architecture

- **Web**: ASP.NET Core / Blazor creator workspace.
- **API**: versioned application API.
- **Application / Domain**: use cases, policies, and business rules.
- **Infrastructure**: SQL Server persistence, storage, queues, and provider adapters.
- **Workers**: durable background orchestration.
- **AI services**: Python FastAPI service with a configurable OpenAI-compatible LLM runtime for source analysis, Vietnamese variant generation and evidence-grounded style synthesis. Trained ML and media speech pipelines are separate follow-on capabilities.
- **Media engine**: Windows After Effects + FFmpeg render worker for vertical 1080×1920 MP4 variants; actual quality/performance still requires hardware acceptance.
- **Tests**: unit, integration, and contract tests.

## Principles

1. Rights and provenance are first-class data. Users must confirm they have permission before upload; this confirmation is not an automated legal-rights verification.
2. No platform scraping or publishing bypass is assumed. Integrations must use permitted sources and official APIs.
3. AI output is a draft. Human review is required before publishing.
4. Video render jobs are durably stored and retried; connector orchestration, comprehensive idempotency and end-to-end observability remain release work.
5. Secrets are supplied through environment variables or a secret manager, never committed.

## Getting started

See the [latest repository audit](docs/operations/RELEASE_AUDIT_2026-11-02.md), [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [docs/ROADMAP.md](docs/ROADMAP.md). For the isolated Windows SQL Server test host and self-hosted runner, follow [docs/SQLSERVER_QC_RUNNER.md](docs/SQLSERVER_QC_RUNNER.md). The selected SaaS/hybrid deployment profile is documented in [docs/SAAS_HYBRID_ARCHITECTURE.md](docs/SAAS_HYBRID_ARCHITECTURE.md), [docs/DEPLOYMENT_GUIDE.md](docs/DEPLOYMENT_GUIDE.md), [docs/DEPLOYMENT_RUNBOOK.md](docs/DEPLOYMENT_RUNBOOK.md), and [docs/PUBLIC_LAUNCH_CHECKLIST.md](docs/PUBLIC_LAUNCH_CHECKLIST.md).

## Status

Foundation plus first LLM runtime milestone and an initial automated video-render pipeline. The Python AI service makes provider calls when configured and returns review-required drafts. Discovery ranking is currently heuristic; query planning is not a live data collector, and feedback aggregation is not yet an activated end-to-end learning loop. The video workflow stores source assets and render jobs in SQL, uses After Effects to render a PNG sequence, and FFmpeg to produce H.264/AAC MP4 variants with preview/download routes. Migration 018–020 strengthens same-tenant relationships and workspace-scoped uniqueness. Actual rendering, SQL migration execution, multi-tenant negative tests, and target-machine performance still require acceptance. Live trend connectors, speech/music analysis, advanced effects, product/cart attribution and official publishing are not complete. See [AI Runtime design](docs/design/AI_RUNTIME.md), [video rendering runbook](docs/operations/VIDEO_RENDERING_AE_FFMPEG.md), [go-live gates](docs/operations/GO_LIVE_READINESS.md) and [as-built status](docs/design/AS_BUILT_STATUS.md).
