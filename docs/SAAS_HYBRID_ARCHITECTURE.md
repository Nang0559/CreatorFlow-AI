# CreatorFlow SaaS + Hybrid Architecture

**Decision record:** SaaS product, hybrid infrastructure, both TikTok (Vietnam) and Douyin (China) account providers.
**Status:** target architecture and release constraints; not a claim that all components are implemented.

## 1. Product and tenancy model

CreatorFlow is a multi-tenant SaaS. A tenant is a customer workspace (individual creator, brand or agency). A tenant may have multiple users, creator profiles, projects and platform connections.

Required ownership chain:

`Tenant -> Membership/User -> CreatorProfile -> PlatformConnection`
`Tenant -> ContentProject -> Asset/SourceEvidence -> Experiment/Variant -> PublishJob -> OutcomeObservation`

Every tenant-owned row must carry a non-null `TenantId` (or have an explicit, tested ownership path). Every API query, command, background job, object-storage operation and provider callback must enforce tenant ownership. Never trust a caller-supplied TenantId as proof of access; derive tenant context from the authenticated principal and verified membership.

Minimum roles:
- Owner: billing, tenant settings, membership and integrations.
- Admin: workspace configuration, members and integrations (subject to owner policy).
- Editor: projects, scripts, variants, assets and review.
- Analyst: read-only metrics, trends and reports.
- Publisher: connect/manage authorized accounts and approve publishing.
- Service identity: narrowly scoped worker-to-service permissions, never interactive login.

A user can belong to multiple tenants. A platform account connection belongs to one tenant and has a unique provider + provider-account identity within that tenant. Sharing one platform account across tenants must be an explicit, auditable delegation feature, not an accidental consequence of duplicate rows.

## 2. Hybrid deployment decision

Keep the control plane in a cloud region close to the initial customer base (Vietnam/Asia region where available):
- Public edge: DNS, TLS termination, WAF, rate limiting.
- SaaS control plane: Blazor Web, .NET API, identity integration and admin/billing surface.
- Data plane: SQL Server, private object storage, secrets manager and audit/telemetry.
- Private compute plane: Python AI adapter and later media/ML workers, deployed on a private cloud subnet or customer/owner-managed GPU/CPU node.
- Provider plane: TikTok and Douyin official APIs through separate outbound connector adapters.

The first deployment should use cloud CPU workers, not an on-premises GPU. The hybrid boundary is an interface and network-security boundary, not a requirement to purchase hardware immediately. A private/on-prem worker can be added later through an outbound mTLS agent that pulls signed jobs; do not open inbound access from the internet to the private machine.

Only edge ingress is public. SQL Server, queue, object-storage administration, Python AI and worker control endpoints stay private. Use private endpoints/VPN or mutually authenticated service connections between cloud and private compute.

## 3. Logical architecture

```text
Creator browser / mobile
        |
    DNS + WAF + TLS
        |
   Blazor Web / BFF
        |
   .NET API + Identity
        |
        +---- Tenant authorization + audit
        |
        +---- SQL Server (tenant-scoped metadata)
        |
        +---- Object storage (private media/evidence)
        |
        +---- Outbox -> durable queue -> workers
        |                           |
        |                           +-- Python AI adapter / LLM provider
        |                           +-- media worker (FFmpeg/STT later)
        |                           +-- trend ingestion worker
        |                           +-- analytics/reconciliation worker
        |
        +---- Provider adapters
                  +-- TikTok OAuth/API (separate app + tokens)
                  +-- Douyin OAuth/API (separate app + tokens)
```

The outbox/queue/workers, identity/tenant authorization, media store and platform adapters are target components. Current repository status is recorded in [AS_BUILT_STATUS.md](design/AS_BUILT_STATUS.md).

## 4. Platform account isolation: TikTok + Douyin

Implement a provider-neutral contract with provider-specific implementations. Do not create a single generic token or assume the APIs have matching scopes or capabilities.

Each connection record needs:
- `TenantId`, `Provider` (TikTok or Douyin), provider account ID, display label.
- OAuth client/app reference, encrypted access and refresh tokens, token expiry.
- Granted scopes, authorization time, last successful refresh, connection status.
- Consent evidence, revocation time and audit actor.
- Environment (development/staging/production); never reuse sandbox tokens in production.

Store secrets in a managed secret vault and tokens encrypted with envelope encryption/KMS. Database rows hold ciphertext and key version, not plaintext. Redact tokens from logs and traces. Support disconnect/revoke, refresh failure, reauthorization and deletion workflows.

Provider adapter responsibilities:
- OAuth authorization URL/state/PKCE where supported, callback validation and CSRF protection.
- Scope/capability discovery and account identity confirmation.
- Rate-limit-aware API calls, retry only safe/idempotent operations, pagination and cursor checkpoints.
- Provider request IDs, source provenance, observation timestamps and normalized error codes.
- Webhook signature verification if webhooks are introduced.
- Contract tests with recorded fixtures; sandbox/live tests only with explicitly authorized accounts.

TikTok and Douyin use separate developer apps, review processes, OAuth callbacks, scopes and API clients. Capability availability must be checked per app and region. Do not promise automatic trend crawling, analytics or publishing until the corresponding permission is approved and the adapter passes contract tests.

## 5. Rights, trend and publishing safety

- A source item is not ingestible merely because a public URL exists. Persist source URL/ID, provider, capture time, permitted-use basis, evidence, territory, expiry and allowed operations.
- Distinguish metadata/metrics observation from downloading or reusing media.
- Similarity/saturation signals are advisory and must include evidence, coverage and time window.
- Never present an account suspension/ban probability until verified enforcement outcomes support a calibrated model. Display unknown/insufficient evidence when coverage is weak.
- AI outputs remain drafts. Require a human review gate for script, asset rights and final rendered video.
- Publishing must be initiated by an authorized tenant user, use the tenant's own platform connection, be idempotent, and reconcile provider post status after timeouts.

## 6. Data isolation and security invariants

1. Tenant context is resolved from the authenticated identity and membership, not a request body/header alone.
2. Repository methods require tenant context; avoid unscoped `GetById` methods for tenant-owned entities.
3. Enforce tenant filters in EF Core as defense in depth, with explicit bypass only for audited system jobs.
4. Prefer SQL Server row-level security only as an additional guard, not the only authorization layer.
5. Unique constraints include TenantId where uniqueness is tenant-local.
6. Background jobs carry TenantId, actor/service identity, correlation ID and idempotency key; workers re-check ownership before side effects.
7. Object keys include opaque tenant namespace; authorize every signed URL and use short expiry.
8. Provider callbacks resolve connection identity and verify signatures/state before changing tenant data.
9. Audit security-sensitive changes, OAuth lifecycle, rights decisions, approvals and publishing.
10. Add cross-tenant negative tests for every new API, repository and storage adapter.

## 7. Resilience and operational design

- Use transactional outbox for durable job publication after database commit.
- Queue messages are at-least-once; handlers must be idempotent with a persisted job key.
- Retry transient failures with bounded exponential backoff and jitter; do not retry authorization, validation or rights failures blindly.
- Dead-letter exhausted jobs with replay tooling requiring operator authorization.
- Store job states: queued, running, retry_wait, succeeded, failed, cancelled; expose progress without leaking another tenant's data.
- Apply provider-specific concurrency/rate budgets and circuit breakers.
- Use structured logs, traces and metrics with tenant IDs hashed/pseudonymized where appropriate; never log tokens or raw sensitive media.
- Define RPO/RTO, backup retention, restore tests, incident response and deletion SLA before public launch.

## 8. Deployment tiers and scaling

Pilot:
- 2+ app replicas are not mandatory for a private pilot, but deploy behind a reverse proxy.
- Managed SQL Server or a dedicated private database instance.
- Private object storage; one worker instance; CPU-only AI adapter.
- Automated backup and a verified restore.

Public SaaS:
- Stateless Web/API replicas behind load balancer.
- Separate SQL Server managed HA/backup tier.
- Durable queue and independently scaled worker pools.
- Private object storage with lifecycle policies/CDN only for approved public renditions.
- Dedicated AI/media worker pool with quotas per tenant.
- Staging environment mirroring production topology.
- Central secret management, monitoring, alerting and deployment rollback.

Capacity is driven by concurrent users, media minutes, job throughput, retention and LLM calls. Measure before scaling; do not infer capacity from vCPU estimates alone.

## 9. Architecture implementation gates

- [ ] Identity provider integration, user identity and MFA for privileged users.
- [ ] Tenant, membership and role domain model + migrations.
- [ ] Tenant-scoped repository contracts, EF filters and cross-tenant tests.
- [ ] Object storage adapter, malware/file validation and signed URL authorization.
- [ ] Secret vault and encrypted per-tenant provider tokens.
- [ ] TikTok and Douyin provider contracts and isolated OAuth clients.
- [ ] Durable queue, transactional outbox, idempotent workers and dead-letter handling.
- [ ] Rights/provenance registry and deletion/retention controls.
- [ ] Human approval, publish idempotency and provider reconciliation.
- [ ] Observability, backup/restore drills, security and load testing.
