# Architecture

## Product decision

CreatorFlow targets a **multi-tenant SaaS** delivered on **hybrid infrastructure**, with separate official integrations for **TikTok Vietnam and Douyin China**. Each tenant connects accounts it owns or is authorized to manage. Operator accounts are not shared customer credentials.

See [SaaS + Hybrid Architecture](SAAS_HYBRID_ARCHITECTURE.md) for tenancy, provider boundaries, network segmentation, rights and resilience invariants.

## Boundaries

- Creator workspace: editorial review and creator actions.
- API: authenticated, versioned application contracts.
- Identity/tenant: configurable OIDC validation, issuer+subject membership resolution, tenant query filters and write ownership checks are implemented as a foundation; production identity configuration, complete role policy and security acceptance remain open.
- Domain: content lifecycle invariants, independent of infrastructure.
- Application: use cases coordinated through ports.
- Infrastructure: SQL Server, private object storage, durable queue and provider adapters.
- Workers: durable asynchronous orchestration with idempotency and bounded retries.
- AI services: model capabilities behind provider-neutral interfaces; output is always a draft.
- Media engine: Windows After Effects project/PNG-sequence rendering plus FFmpeg H.264/AAC MP4 encoding through SQL-backed video jobs; advanced effects, STT/TTS and hardware acceptance remain open.
- Platform connectors: separate TikTok and Douyin OAuth clients, token stores, scopes, rate limits and audit trails.

## Target workflow

Idea or permitted source -> rights/provenance check -> trend/context analysis -> script draft -> human script approval -> narration/subtitles -> render -> human final approval -> official provider publishing API -> analytics/reconciliation.

## Hybrid deployment

Public cloud edge (WAF/TLS) fronts stateless Web/API. SQL Server, queue and object storage remain private. Python AI and media/trend workers run in a private compute plane. Optional private/on-prem compute is outbound-only over VPN/mTLS. No database, queue, AI or worker management endpoint is public.

## Non-functional rules

- No unauthorized scraping, watermark removal or platform-policy evasion.
- Do not process source assets without documented rights/permission.
- Secrets never enter source control, browser storage or logs.
- Store media outside web root; validate MIME, size, duration and file signatures.
- Enforce authorization at API, database, job and storage boundaries.
- Derive tenant context from verified identity and membership, never caller-supplied TenantId alone.
- Require explicit human approval before publishing.
- Use transactional outbox, durable queue and idempotent workers for long-running work once implemented.
- Keep provider permissions and token records isolated between TikTok and Douyin.

## Current implementation boundary

The current branch has .NET API/Blazor, SQL Server migrations, a Python FastAPI LLM adapter, tenant identity/isolation foundations, a user-shared-link inbox and a local Windows AE+FFmpeg render worker. It does not yet implement production OIDC configuration, full RLS/negative-test coverage, cloud object storage, live TikTok/Douyin trend providers, the complete Discovery feedback-learning loop, speech/music analysis, product/cart attribution or publishing. Video rendering is a local pilot pipeline, not a production media platform. The Style Learning API has known profile-scoping limitations described in [as-built status](design/AS_BUILT_STATUS.md). Do not infer implementation from this target architecture.
