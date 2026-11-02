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
- **Media engine**: FFmpeg-based, deterministic media processing.
- **Tests**: unit, integration, and contract tests.

## Principles

1. Rights and provenance are first-class data. Only assets with a recorded permitted-use basis can enter production.
2. No platform scraping or publishing bypass is assumed. Integrations must use permitted sources and official APIs.
3. AI output is a draft. Human review is required before publishing.
4. Every long-running operation is observable, retryable, idempotent, and auditable.
5. Secrets are supplied through environment variables or a secret manager, never committed.

## Getting started

See [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [docs/ROADMAP.md](docs/ROADMAP.md). For the isolated Windows SQL Server test host and self-hosted runner, follow [docs/SQLSERVER_QC_RUNNER.md](docs/SQLSERVER_QC_RUNNER.md). The selected SaaS/hybrid deployment profile is documented in [docs/SAAS_HYBRID_ARCHITECTURE.md](docs/SAAS_HYBRID_ARCHITECTURE.md), [docs/DEPLOYMENT_GUIDE.md](docs/DEPLOYMENT_GUIDE.md), [docs/DEPLOYMENT_RUNBOOK.md](docs/DEPLOYMENT_RUNBOOK.md), and [docs/PUBLIC_LAUNCH_CHECKLIST.md](docs/PUBLIC_LAUNCH_CHECKLIST.md).

## Status

Foundation plus first LLM runtime milestone. The Python AI service now makes real provider calls when configured; it fails closed without credentials and returns review-required structured drafts. The runtime is reachable through authenticated .NET API proxy endpoints; Style Lab invokes source analysis and variant generation, and Style Profiles can request profile-scoped learning synthesis. Provider credentials are required for real model calls. Trained ML, durable jobs, platform connectors, media rendering and publishing remain outstanding. See [AI Runtime design](docs/design/AI_RUNTIME.md) and [as-built status](docs/design/AS_BUILT_STATUS.md).
