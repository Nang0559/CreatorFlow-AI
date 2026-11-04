# CreatorFlow SaaS Hybrid Deployment Runbook

**Target:** multi-tenant SaaS, hybrid compute, TikTok Vietnam + Douyin China.
**Release status:** operational plan. Do not treat this runbook as proof that missing production components are implemented.

## 1. Environment layout

Maintain three isolated environments:
- **Development:** synthetic/test data, developer OAuth apps, no production customer tokens.
- **Staging:** production-like topology, separate database/storage/keys, platform sandbox or explicitly authorized test accounts.
- **Production:** approved OAuth apps, customer data, restricted operator access and production secrets.

Never promote database backups, OAuth tokens or media from production into development. Use sanitized fixtures.

## 2. Network and host topology

Recommended cloud layout:
- Public subnet: load balancer/WAF only.
- Private application subnet: Blazor Web and .NET API.
- Private data subnet: SQL Server, queue and object storage endpoints.
- Private compute subnet: Python AI service and workers.
- Optional private/on-prem compute node: outbound-only mTLS job agent over VPN/private link.

Inbound internet access: HTTPS 443 to edge only. Redirect HTTP 80 to HTTPS if needed. Deny public inbound access to SQL Server, queue, Python service, worker endpoints and storage administration. Restrict outbound traffic to approved LLM and TikTok/Douyin API endpoints where operationally possible.

## 3. Initial sizing (planning baseline, not a capacity guarantee)

| Component | Pilot | Public SaaS starting point |
|---|---:|---:|
| Web/API | 4 vCPU / 8 GB RAM | 2 replicas, each 4 vCPU / 8–16 GB |
| AI adapter | 2–4 vCPU / 4–8 GB | 4–8 vCPU / 8–16 GB |
| SQL Server | managed or 4 vCPU / 16 GB | managed HA, start sizing test at 8 vCPU / 32 GB |
| Worker | 4 vCPU / 8 GB | independently scaled 8 vCPU / 16 GB |
| OS/app SSD | 100–250 GB | separate volumes; media in object storage |
| GPU | Not required for current remote-LLM design | Only after measured local-model need |

Start CPU-only. Put media in object storage, not the application disk. Set SQL storage, IOPS and retention based on measured workload. Load-test expected tenant concurrency and media volume before production capacity approval.

## 4. Required managed services

Before launch provision:
- DNS/domain and managed TLS certificate.
- WAF/reverse proxy/load balancer.
- SQL Server with private networking, encryption and automated backups.
- Private object storage with lifecycle and versioning policy.
- Secret manager/KMS for application secrets and token encryption keys.
- Durable queue/outbox for discovery/provider orchestration; the local video render worker already uses SQL-backed jobs but is not a substitute for the production outbox/queue plane.
- Central logs, metrics, tracing and alerting.
- Email delivery for account verification, password reset and security alerts.
- Identity provider supporting OIDC, MFA and account lifecycle.
- VPN/private link for optional private compute.

## 5. Application configuration

Use separate secret sets per environment. The current repository's bootstrap settings are not a production multi-tenant identity design.

.NET API (current keys; verify exact configuration binding in the deployed build):
- `ConnectionStrings__CreatorFlow`
- `Authentication__ApiKey` (bootstrap only; do not use as the public SaaS user identity)
- `CREATORFLOW_AI_BASE_URL`
- `CREATORFLOW_AI_API_KEY`

Blazor Web:
- `CreatorFlowApi__BaseUrl`
- `CreatorFlowApi__ApiKey` (server-side only; never sent to browser JavaScript)

Python AI:
- `CREATORFLOW_AI_API_KEY` (shared service-to-service secret; must match the value configured for the API's AI client and the AI container).
- `CREATORFLOW_LLM_PRIMARY_PROVIDER`, `CREATORFLOW_LLM_FALLBACK_PROVIDERS`.
- `CREATORFLOW_LLM_GEMINI_API_KEY`, `CREATORFLOW_LLM_OPENAI_API_KEY`, and/or local Ollama endpoint/model according to the selected providers.

Current local-pilot discovery connectors (API runs as a Windows process):
- `BraveSearch__ApiKey` for Brave Search Web API; alternatively configure it in the workspace Settings UI after migration 028.
- `TikTokResearch__ClientKey` and `TikTokResearch__ClientSecret` only for a TikTok Research API project that has actually been approved and is eligible for the applicant. Do not assume ordinary TikTok developer access grants Research API.

The current local `.env.example` also includes `Authentication__ApiKey` and `CreatorFlowApi__ApiKey`; these are the API/Web bootstrap shared secret pair and must have the same value. The Keycloak client secret, Keycloak admin password and one-time owner bootstrap token are separate credentials and are not provider API keys.

The current workspace AI provider registry (migration 024) and discovery connector settings (migration 028) allow Owner/Admin to manage provider keys in the authenticated Settings UI. Workspace secrets are encrypted server-side and never returned to the browser. The internal service credentials, Keycloak bootstrap secrets and database connection string remain deployment configuration, not workspace connector fields.

TikTok/Douyin user OAuth connections, publishing scopes and a commercial-grade global TikTok/Douyin trend feed are not delivered by these API keys. Do not create arbitrary `TIKTOK_CLIENT_KEY` or `DOUYIN_CLIENT_KEY` variables and assume connectors work; the corresponding OAuth adapters/routes must first be implemented and tested. Store future platform user tokens per tenant connection in encrypted persistence backed by a production KMS.

## 6. Database release procedure

1. Provision an empty dedicated database for the environment.
2. Back up before each production migration; verify restore procedure.
3. Apply numbered SQL migrations in order using a deployment identity with DDL permission. Migration 011 creates the initial Tenants and TenantMemberships tables; it does not make legacy business tables tenant-safe.
4. Run schema verification and migration integration tests against both a clean database and supported upgrade baseline.
5. Deploy API/Web only after schema checks pass.
6. Run health checks and read/write smoke tests using a non-production tenant.
7. Keep rollback scripts or forward-fix plan for every schema change; do not assume destructive down-migrations are safe.
8. Record migration version, commit SHA, operator and timestamp.

Migration 010 still requires SQL Server execution tests against an empty database and a schema-009 upgrade database. Migration 011 also needs clean/upgrade execution tests before production. Do not onboard external tenants until all required tenant migrations, production OIDC, full role policies, SQL RLS and cross-tenant negative tests pass. Discovery SQL tables created in migrations 014–015 are not yet tenant-scoped.

## 7. Deploy sequence

1. Confirm release branch, commit SHA, CI status and signed-off change list.
2. Confirm .NET 10 LTS migration is complete before public release; current branch baseline is .NET 9.
3. Provision environment, network boundaries, database, object storage, secret manager and monitoring.
4. Apply database migrations and verify schema.
5. Deploy API and Web containers/artifacts; verify health and readiness endpoints.
6. Deploy private AI service; verify service-to-service authentication and provider health.
7. For local pilot only, start the SQL-backed FFmpeg-only render worker using `docs/operations/VIDEO_RENDERING_AE_FFMPEG.md`. Deploy cloud discovery/provider workers only after durable outbox/queue, idempotency and dead-letter controls exist.
8. Enable tenant identity and authorization before onboarding external customers.
9. Register TikTok and Douyin applications separately; complete platform reviews and request only required scopes.
10. Connect one authorized test account per provider in staging; test token refresh/revocation and provider error handling.
11. Run rights, approval and publishing tests with private/test visibility first.
12. Enable production features by tenant/provider feature flags; monitor and retain rollback capability.

## 8. Health, monitoring and alerting

Expose liveness/readiness separately when the hosting platform supports it. Monitor:
- API availability, p50/p95 latency, 4xx/5xx and authentication failures.
- SQL connection pool, query latency, deadlocks, storage/backup status.
- Queue depth, oldest job age, retries, dead-letter count and worker heartbeat.
- AI provider latency, token/cost usage, timeout and structured-output validation failures.
- OAuth refresh failures, revoked grants, provider rate limits and connector lag.
- Tenant-level quotas, media storage growth, deletion backlog and unusual access patterns.

Never put OAuth tokens, API keys, full signed URLs, private media or sensitive prompt payloads in logs.

## 9. Rollback and incident response

- Keep previous application artifact and deployment manifest.
- Prefer backward-compatible expand/contract database changes.
- Disable risky connector/publishing features using feature flags before rolling back code.
- Stop workers before schema rollback if a job payload/schema is incompatible.
- Revoke compromised platform credentials and rotate service secrets immediately.
- Preserve audit evidence; notify affected tenants under the incident policy.
- Restore from backup only after defining data-loss window and validating tenant impact.

## 10. Operational acceptance

A deployment is accepted only when health checks, schema verification, auth/tenant isolation, storage access, backup/restore, monitoring, and rollback are evidenced. A green build alone is not a production deployment sign-off.
