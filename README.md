# CreatorFlow AI

AI-powered content intelligence and automated video production platform.

CreatorFlow AI turns a creator-approved idea or licensed source asset into a reviewed, localized, edited, and publish-ready video package. The platform is designed around explicit rights tracking, human approval gates, reproducible media jobs, and official publishing integrations.

## Initial architecture

- **Web**: ASP.NET Core / Blazor creator workspace.
- **API**: versioned application API.
- **Application / Domain**: use cases, policies, and business rules.
- **Infrastructure**: SQL Server persistence, storage, queues, and provider adapters.
- **Workers**: durable background orchestration.
- **AI services**: Python services for trend analysis, content generation, translation, speech, and recommendations.
- **Media engine**: FFmpeg-based, deterministic media processing.
- **Tests**: unit, integration, and contract tests.

## Principles

1. Rights and provenance are first-class data. Only assets with a recorded permitted-use basis can enter production.
2. No platform scraping or publishing bypass is assumed. Integrations must use permitted sources and official APIs.
3. AI output is a draft. Human review is required before publishing.
4. Every long-running operation is observable, retryable, idempotent, and auditable.
5. Secrets are supplied through environment variables or a secret manager, never committed.

## Getting started

See [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and [docs/ROADMAP.md](docs/ROADMAP.md).

## Status

Bootstrap phase. The first commit establishes architecture, contracts, local development conventions, and CI. Provider integrations and production deployment are implemented incrementally behind interfaces.
