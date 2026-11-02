# CreatorFlow AI — Deployment and Public Launch Guide

**Selected deployment profile:** SaaS multi-tenant + hybrid infrastructure + both TikTok Vietnam and Douyin China.
**Status:** planning and release guidance for the current bootstrap; not a claim that all production modules are implemented.

## 1. Chosen operating model

CreatorFlow is intended to be a multi-tenant SaaS. Each customer has an isolated workspace, members, creator profiles, projects, assets and platform connections. Customers connect accounts they own or are explicitly authorized to manage. The operator's TikTok/Douyin accounts are for platform validation and internal dogfooding, not shared credentials for all customers.

Hybrid means the public SaaS control plane runs in cloud infrastructure, while AI/media compute is isolated in a private compute plane. Initially use cloud CPU workers; a private/on-prem worker can be added later as an outbound-only mTLS agent. Do not expose internal services to the internet.

Use separate TikTok and Douyin developer applications, OAuth callbacks, scopes, encrypted token records, provider adapters and audit trails. Never assume an account or token from one platform authorizes the other.

Detailed decisions:
- [SaaS + Hybrid Architecture](SAAS_HYBRID_ARCHITECTURE.md)
- [Deployment Runbook](DEPLOYMENT_RUNBOOK.md)
- [Public Launch Checklist](PUBLIC_LAUNCH_CHECKLIST.md)

## 2. What exists today

The repository currently contains:
- .NET API and Blazor Server workspace.
- SQL Server persistence for implemented project, trend, Style Lab, style-learning and Trend Selection foundations.
- Python FastAPI AI service with OpenAI-compatible LLM calls.
- API-key service authentication for isolated Development bootstrap only.
- OIDC Web login (Authorization Code + PKCE), protected cookie sessions, issuer+subject identity mapping, tenant selection and membership lifecycle API.
- SQL-backed video asset/render jobs with a local Windows After Effects + FFmpeg worker, authenticated preview/download and atomic 2–3-style job creation.
- Tenant-scoped Share Inbox for creator-submitted URLs; no server-side URL fetching or automatic creator follow.
- CI for .NET build/tests, Python tests and PowerShell script parsing.

The tenant identity foundation and baseline isolation are implemented in code, but a production OIDC provider must still be configured and contract-tested; MFA policy, invitation email delivery, full per-operation RBAC, SQL Server migration validation and cross-tenant negative tests remain release gates. TikTok/Douyin OAuth and provider adapters, live trend collection, the complete Discovery feedback-learning loop, discovery/provider job orchestration, cloud object storage, similarity/saturation engines, rights registry, calibrated publishing/account risk, and official publishing/analytics are not production-ready. The local video render worker is a pilot capability, not the production cloud media plane.

## 3. Platform account setup

### 3.1 TikTok Vietnam

Prepare a TikTok account that you own for internal validation and create a separate TikTok for Developers application. A normal TikTok login is not an API credential. Configure the Content Posting API and request only the scopes required by implemented features. For Direct Post, the official API requires the approved `video.publish` scope, user authorization, creator-info query and explicit user consent. Unaudited clients are restricted to private visibility. See [TikTok Direct Post](https://developers.tiktok.com/docs/en/content-posting-api-reference-direct-post) and [Get Started](https://developers.tiktok.com/docs/en/content-posting-api-get-started).

For SaaS, every customer authorizes their own account via OAuth. Store each tenant's grants separately, encrypted server-side, with expiry, granted scopes, refresh and revocation state.

### 3.2 Douyin China

Prepare a Douyin account you own for internal validation and a separate Douyin Open Platform developer application. Douyin is not TikTok's alternate endpoint. Its video-create API documents a distinct `video.create` scope and user authorization. Maintain separate OAuth clients, tokens, scopes, callback validation and provider adapter. See [Douyin Open Platform — Create video](https://open.douyin.com/platform/resource/docs/openapi/video-management/douyin/create/create-video) and [OAuth authorization](https://open.douyin.com/platform/doc/6848806488699019271/).

### 3.3 Trend discovery permissions

Owning an account does not grant permission to bulk collect public content. Use an eligible official API, licensed provider/dataset, creator-submitted links or manually curated observations. TikTok Research Tools are for eligible independent/non-profit academic or public-interest research, not a normal commercial creator SaaS; a developer account alone is insufficient. Do not make commercial trend discovery depend on Research API access. See [Research Tools eligibility](https://developers.tiktok.com/docs/en/about-research-api) and [Research API FAQ](https://developers.tiktok.com/docs/en/research-api-faq).

## 4. Hybrid deployment topology

```text
Browser -> DNS/WAF/TLS -> Blazor Web / .NET API
                               |
                 +-------------+-------------+
                 |             |             |
             SQL Server   Private object   Outbox/queue
                          storage              |
                                         worker pools
                                       /      |       \
                                   Python   Media   Connectors
                                    AI     FFmpeg   TikTok/Douyin
```

Cloud control plane: edge, Web/API, identity, SQL, object storage, secrets, audit and observability. Private compute plane: AI adapter and later media/trend/analytics workers. Optional owner/customer-hosted compute connects outbound over VPN/mTLS; no inbound public port. SQL, queue, storage admin and Python service are private-only.

## 5. Server sizing

These are initial planning estimates, not measured capacity guarantees. Load-test expected tenant concurrency, media minutes, scan cadence, retention and model/provider calls before production.

| Component | Pilot | Public SaaS starting point |
|---|---:|---:|
| Web/API | 4 vCPU / 8 GB | 2 replicas, each 4–8 vCPU / 8–16 GB |
| Python AI adapter | 2–4 vCPU / 4–8 GB | 4–8 vCPU / 8–16 GB |
| SQL Server | managed or 4 vCPU / 16 GB | managed HA; test from 8 vCPU / 32 GB |
| Worker | 4 vCPU / 8 GB | independently scaled 8 vCPU / 16–32 GB |
| App/log SSD | 100–250 GB | separate volumes; media in object storage |
| GPU | Not required | Only after benchmarked local inference need |

Start CPU-only. The current Python service calls an OpenAI-compatible remote LLM endpoint; the repository does not currently require local GPU inference or a trained ML stack. Do not buy a GPU solely because this is an AI product.

## 6. Runtime and machine learning

Current branch baseline is .NET 9 and Python 3.11+. **Public launch gate:** migrate to .NET 10 LTS before .NET 9 support ends on 10 November 2026. SQL Server 2022 or a supported managed equivalent is the current deployment target.

Current Python AI service dependencies are FastAPI, Uvicorn, Pydantic and HTTPX. PyTorch, TensorFlow, CUDA, scikit-learn, XGBoost and LightGBM are not required by current endpoints.

Add ML components only when corresponding datasets and evaluation gates exist:
- scikit-learn for preference-learning baseline after versioned feature extraction.
- LightGBM/XGBoost only after verified outcome observations and time/account-safe splits.
- sentence-transformers + vector index for semantic similarity.
- FFmpeg/OpenCV/perceptual hashes for authorized media/frame comparison.
- Hosted speech API first; faster-whisper only if local transcription is justified.
- MLflow and Evidently when models are trained, versioned and monitored.

Keep Select/Reject/Watch distinct. Watch is uncertain, not a negative label. Do not show numeric account-ban risk until verified enforcement outcomes support calibration.

## 7. Secrets and configuration

Use a secret manager per environment. Never commit production credentials or place service keys in browser code.

Current bootstrap settings (verify exact binding in deployed build):
- Web OIDC: `Authentication:Oidc:Authority`, `Authentication:Oidc:ClientId`, `Authentication:Oidc:ClientSecret`, `Authentication:Oidc:ApiScope`.
- API OIDC: `Authentication:Oidc:Authority`, `Authentication:Oidc:Audience`.
- First Owner: `Authentication:OwnerBootstrapToken` (one-time secret; never commit it).
- API: `ConnectionStrings__CreatorFlow`, `Authentication__ApiKey`, `CREATORFLOW_AI_BASE_URL`, `CREATORFLOW_AI_API_KEY`.
- Web: `CreatorFlowApi__BaseUrl`, `CreatorFlowApi__ApiKey` (server-side only).
- Python: `CREATORFLOW_AI_API_KEY`, `CREATORFLOW_LLM_API_KEY`, `CREATORFLOW_LLM_MODEL`.

The shared API key is bootstrap service authentication, not customer identity. Do not expose a public SaaS using it as the tenant/user authorization model.

Future platform secrets must be separate per provider and environment: client key/secret, exact redirect URI, approved scopes and KMS-backed token encryption. Store user tokens as encrypted per-tenant connection data; do not put user refresh tokens in global environment variables.

## 8. SaaS security and operations release gates

Before inviting external customers:
- Configure and contract-test the production OIDC provider; enforce MFA for Owner/Admin at the provider; verify email_verified, API audience/scope and logout callbacks.
- Complete fine-grained RBAC, invitation email delivery and central session revocation.
- Tenant-scoped persistence/repositories, EF filters and cross-tenant negative tests.
- Per-tenant platform OAuth connections, token encryption, consent, revocation and audit.
- Rights/provenance evidence, retention/deletion and media validation.
- Private object storage, signed short-lived URLs, backup and restore drills.
- Durable outbox/queue/workers, idempotency, bounded retries and dead-letter handling before enabling async jobs.
- Rate limits, tenant quotas, WAF, SSRF protection, monitoring, alerting and incident response.
- Human approval before publishing; provider idempotency and post reconciliation.
- SQL migration 010 execution tests against an empty database and schema-009 upgrade database.

Follow [Deployment Runbook](DEPLOYMENT_RUNBOOK.md) in each environment and use [Public Launch Checklist](PUBLIC_LAUNCH_CHECKLIST.md) as the release sign-off. Current repository status remains a pilot foundation, not a public multi-tenant SaaS release. See [OIDC identity setup](IDENTITY_SETUP.md) for account bootstrap and member lifecycle.
