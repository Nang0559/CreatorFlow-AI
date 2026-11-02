# Public SaaS Launch Checklist — Hybrid / TikTok + Douyin

Use this as a release gate. Mark an item complete only with a linked test, configuration record, approval or operational evidence. "Planned" and "implemented in part" are not complete.

## A. Product and legal readiness
- [ ] Define SaaS plans, quotas, billing, trial, cancellation and support SLA.
- [ ] Publish Terms of Service, Privacy Notice, acceptable-use policy and data-processing terms.
- [ ] Define data controller/processor roles, cross-border data transfers and retention/deletion policy.
- [ ] Review Vietnam and China market requirements with qualified counsel; determine hosting/data residency obligations before storing China-user data.
- [ ] Define content ownership, license evidence and prohibited-content handling.
- [ ] Establish incident response, abuse reporting and customer support ownership.

## B. SaaS identity and tenant isolation — BLOCKER
- [ ] OIDC login, email verification, password recovery and MFA for privileged accounts.
- [ ] Tenant creation, invitations, membership lifecycle and role-based access.
- [ ] Resolve tenant context from verified identity; no caller-controlled tenant impersonation.
- [ ] TenantId on all tenant-owned records and tenant-scoped indexes/constraints, including unmapped Discovery SQL tables; migrations 018–020 cover currently mapped core relationships only.
- [ ] Cross-tenant API, repository, SQL relationship, object-storage, job and callback negative tests.
- [ ] Admin/support access is time-bound, least-privilege and audited.
- [ ] Rate limits and quotas by tenant/user/API key; replace shared bootstrap API key for customer identity.

## C. TikTok Vietnam + Douyin China integrations
- [ ] Create separate TikTok and Douyin developer applications.
- [ ] Register exact redirect URIs for dev/staging/prod and complete platform verification/review.
- [ ] Request only required, approved scopes; maintain a capability matrix per provider/app.
- [ ] OAuth state/PKCE and callback validation; encrypted per-tenant tokens; refresh and revocation.
- [ ] Confirm account identity and tenant ownership/authorization at connection time.
- [ ] Provider-specific rate limits, cursor checkpoints, retries and audit records.
- [ ] Contract tests and staging tests with accounts explicitly authorized by their owners.
- [ ] Do not use TikTok Research API as the commercial SaaS public-trend data source unless eligibility is independently confirmed.
- [ ] Do not enable publishing until final user approval, provider permission, idempotency and reconciliation pass.

## D. Data, rights and AI safety — BLOCKER
- [ ] Rights/provenance evidence recorded before source media processing.
- [ ] Distinguish public metadata observation from media downloading/reuse.
- [ ] Similarity/saturation assessments expose source coverage and observation windows.
- [ ] Account-enforcement risk stays "unknown" until representative verified outcomes and calibration exist.
- [ ] Human review for generated scripts and rendered media; explicit final publish approval.
- [ ] Feature snapshots are versioned; Select/Reject/Watch semantics preserved; Watch excluded from binary negative labels.
- [ ] AI provider costs, data retention and training-use settings reviewed.

## E. Runtime and infrastructure
- [ ] Upgrade .NET 9 baseline to .NET 10 LTS before public launch.
- [ ] Pin supported runtime, Python dependencies and container base images.
- [ ] Cloud region, private subnet, WAF, TLS, DNS and egress policy approved.
- [ ] SQL Server private endpoint, least-privilege identities, encryption and backups configured.
- [ ] Private object storage, lifecycle, malware scanning and signed URL controls.
- [ ] Secret manager/KMS, key rotation and token encryption tested.
- [ ] Discovery/provider outbox, durable orchestration, dead-letter handling and idempotency tests; the local video render worker is not a substitute for provider orchestration.
- [ ] Hybrid private worker uses outbound mTLS/VPN; no public inbound management port.
- [ ] Capacity/load test based on target concurrency, media throughput and cost budget.
- [ ] No GPU purchase without a benchmarked local inference/media workload.

## F. CI, database and release engineering — BLOCKER
- [ ] CI build and unit/integration tests pass on target runtime.
- [ ] SQL migration 010 executes successfully on an empty SQL Server database.
- [ ] SQL migration 010 upgrades schema 009 successfully.
- [ ] Migration/schema verification and seed strategy are reproducible.
- [ ] Security scanning, dependency review and secret scanning enabled.
- [ ] Staging deploy is repeatable and produces an immutable release artifact.
- [ ] Backup restore drill and rollback/forward-fix plan evidenced.
- [ ] Smoke tests cover login, tenant boundaries, AI draft, media access and platform connection lifecycle.

## G. Observability and operations
- [ ] Logs, metrics, traces, dashboards and actionable alerts.
- [ ] No secrets/tokens/media in logs; retention and access controls verified.
- [ ] Define SLO, RPO, RTO, support hours and incident escalation.
- [ ] Queue/worker lag, provider throttling, AI cost and storage growth alerts.
- [ ] Data export/deletion and tenant offboarding tested.
- [ ] On-call runbook and customer communication templates prepared.

## H. Controlled launch
- [ ] Internal dogfood tenant only.
- [ ] Closed pilot with invited creators and explicit feedback consent.
- [ ] Per-tenant feature flags and quotas; immediate kill switch for each provider.
- [ ] Review error rates, costs, rights incidents and customer outcomes before expanding.
- [ ] Public launch approval signed by engineering, security, product and legal owners.

## Current known blockers from repository as-built record
- Tenant identity, EF filters, tenant stamping and core same-tenant SQL relationships are implemented as foundations; production OIDC, full role policy, RLS, unmapped Discovery ownership and cross-tenant negative tests remain blockers.
- TikTok and Douyin OAuth/provider adapters are not implemented.
- A local SQL-backed AE+FFmpeg video render worker exists; cloud object storage, discovery/provider queue/outbox, dead-letter operations and production media lifecycle are not implemented.
- Rights registry, similarity/saturation engine and calibrated account-risk model are not implemented.
- SQL Server execution/upgrade tests for migrations 010 and 018–020 remain outstanding, including fresh database, existing legacy workspace and rollback-on-cross-tenant-reference scenarios.
- .NET 10 LTS migration is required before public launch.
