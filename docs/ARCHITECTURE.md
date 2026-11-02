# Architecture

## Product decision

CreatorFlow targets a **multi-tenant SaaS** delivered on **hybrid infrastructure**, with separate official integrations for **TikTok Vietnam and Douyin China**. Each tenant connects accounts it owns or is authorized to manage. Operator accounts are not shared customer credentials.

See [SaaS + Hybrid Architecture](SAAS_HYBRID_ARCHITECTURE.md) for tenancy, provider boundaries, network segmentation, rights and resilience invariants.

## Boundaries

- Creator workspace: editorial review and creator actions.
- API: authenticated, versioned application contracts.
- Identity/tenant: user authentication, membership, roles and tenant context (target; not implemented as production SaaS yet).
- Domain: content lifecycle invariants, independent of infrastructure.
- Application: use cases coordinated through ports.
- Infrastructure: SQL Server, private object storage, durable queue and provider adapters.
- Workers: durable asynchronous orchestration with idempotency and bounded retries.
- AI services: model capabilities behind provider-neutral interfaces; output is always a draft.
- Media engine: deterministic FFmpeg operations from validated job specifications.
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

The current bootstrap has .NET API/Blazor, SQL Server foundations and Python FastAPI LLM adapter. It does not yet implement production identity/tenant isolation, object/media storage, durable queue/workers, TikTok/Douyin OAuth/connectors or publishing. The Style Learning API has known profile-scoping limitations described in [as-built status](design/AS_BUILT_STATUS.md). Do not infer implementation from this target architecture.
