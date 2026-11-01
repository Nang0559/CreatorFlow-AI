# CreatorFlow AI — Deployment and Public Launch Guide

**Status:** deployment planning for the current bootstrap; not a claim that all production modules are implemented.

## 1. What exists today

The repository currently contains:
- .NET API and Blazor Server workspace.
- SQL Server persistence for the implemented project, trend, Style Lab, style-learning and Trend Selection foundations.
- Python FastAPI AI service with OpenAI-compatible LLM calls.
- API-key service authentication.
- CI for .NET build/tests and Python tests.

The following are not yet production-ready: multi-user identity/tenant isolation, TikTok/Douyin trend crawling, durable background queue, object/media storage, similarity/saturation engines, rights registry, calibrated account-enforcement risk, and official publishing/analytics adapters. Do not advertise or deploy those as available features until implemented and tested.

## 2. Account and platform setup

### 2.1 TikTok Vietnam account

You should have the TikTok account(s) that you own and intend to connect:
- One primary creator account for publishing and analytics validation.
- Optional additional owned test/brand accounts for testing authorization and account separation.

A normal TikTok login is not an API credential. For an integration, create a TikTok for Developers application, configure the required product and scopes, complete the platform review/audit where required, then let each account owner authorize CreatorFlow using OAuth. Store access/refresh credentials encrypted server-side and support revocation.

For official Direct Post, the Content Posting API requires an approved `video.publish` scope, user authorization, creator-info query and explicit user consent. Un-audited clients are restricted to private visibility. See [TikTok Direct Post](https://developers.tiktok.com/docs/en/content-posting-api-reference-direct-post) and [Get Started](https://developers.tiktok.com/docs/en/content-posting-api-get-started).

### 2.2 Douyin / China account

Douyin and TikTok are separate platform products and authorization surfaces. If CreatorFlow needs to publish to, or read authorized data from, your China account, create/obtain the relevant Douyin Open Platform developer application and request the required permissions separately. The Douyin video-create API documents the `video.create` scope and user authorization; each operation must be made visible to and knowingly initiated by the user. See [Douyin Open Platform — Create video](https://open.douyin.com/platform/resource/docs/openapi/video-management/douyin/create/create-video).

A Douyin account does not automatically authorize TikTok access, and a TikTok developer app/token must not be assumed to work with Douyin. Keep separate OAuth clients, token stores, scopes, provider adapters and audit records.

### 2.3 Do you need both accounts to launch?

No, not for the initial CreatorFlow workspace, manual trend entry, AI-assisted ideation or style experiments. Those can be piloted without either platform account being connected.

You need the corresponding owned account and approved API integration only for the platform-specific actions you enable:
- Publishing to TikTok: TikTok account + TikTok developer app + approved scope + OAuth consent.
- Publishing to Douyin: Douyin account + Douyin Open Platform app + approved permission + OAuth consent.
- Reading own account analytics: the applicable platform product/scope and owner authorization.

For scanning public market trends, owning an account is not by itself permission to bulk collect public content. Use a permitted API/data provider, licensed dataset, creator-submitted links, or manually curated observations. TikTok Research Tools are for eligible independent/non-profit academic or public-interest research, not a normal commercial creator product; a developer account alone is insufficient. Do not make commercial trend scanning depend on Research API access. See [Research Tools eligibility](https://developers.tiktok.com/docs/en/about-research-api) and [Research API FAQ](https://developers.tiktok.com/docs/en/research-api-faq).

## 3. Recommended deployment topology

For a private pilot, one Linux VM can host API, Web and Python AI service in separate containers, with SQL Server managed separately or on a dedicated database VM. For a public multi-user launch, separate the web/API, workers/AI and database tiers.

```text
Browser
  |
HTTPS reverse proxy / WAF
  |
Blazor Web ---- .NET API ---- SQL Server
                    |
                    +---- Python AI service (private network)
                    |
                    +---- Object storage (media/assets)
                    |
                    +---- Durable queue (when worker is implemented)
                              |
                              +---- Background workers
```

Do not expose SQL Server, Redis/queue, object storage admin endpoints or the Python AI service directly to the public internet. Only the reverse proxy should accept public inbound traffic (normally 443; 80 only for HTTPS redirect/certificate validation).

## 4. Server sizing

These are initial planning estimates, not measured capacity guarantees. Load-test with expected users, scan cadence, video volume and model choice before committing to production capacity.

| Tier | Pilot / internal test | Initial public production |
|---|---|---|
| Web + API | 4 vCPU, 8 GB RAM | 4–8 vCPU, 8–16 GB RAM; 2 replicas for availability |
| Python AI adapter | 2–4 vCPU, 4–8 GB RAM | 4–8 vCPU, 8–16 GB RAM; scale separately |
| SQL Server | 4 vCPU, 8–16 GB RAM, SSD | 8 vCPU, 32 GB RAM starting point; managed HA where justified |
| Worker/media processing | 4 vCPU, 8 GB RAM | 8 vCPU, 16–32 GB RAM, independently scalable |
| Storage | 100–250 GB SSD for application/logs; object storage for media | Object storage with lifecycle/backup policies; size from retention and media bitrate |
| Network | 100 Mbps adequate for a small pilot | 1 Gbps uplink preferred where media is frequently transferred |

For an early pilot, a managed SQL Server database is preferable to placing the database on the same VM as the public web tier. Use private networking, TLS, automated backups, point-in-time recovery where available, and a tested restore procedure.

### GPU

**No GPU is required for the current AI implementation.** The Python service calls an OpenAI-compatible remote LLM endpoint; it does not load or train a local foundation model. Start with CPU-only compute.

Only procure a GPU if a later, measured requirement justifies local inference, large-scale embedding or video/image models. Benchmark the exact model, quantization, batch size and concurrency first; GPU memory requirements vary substantially. Do not buy a GPU merely because the product is described as AI.

## 5. Software and runtime prerequisites

Current repository baseline:
- .NET 9 SDK/runtime for the existing projects.
- Python 3.11 or later.
- SQL Server 2022 or a compatible supported SQL Server offering.
- Reverse proxy with TLS (Caddy, Nginx or managed ingress).
- Container runtime/orchestrator if deploying containers.
- FFmpeg only when the media-processing pipeline is introduced.

**Release gate:** .NET 9 support ends on 10 November 2026. Before a public production launch after that date, migrate the solution, CI and deployment images/packages to .NET 10 LTS and rerun the complete test suite. Do not launch a new public service on an out-of-support runtime. See [Microsoft .NET support policy](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).

Python AI service dependencies are currently FastAPI, Uvicorn, Pydantic and HTTPX. The repo does not currently require PyTorch, TensorFlow, CUDA, scikit-learn, XGBoost or LightGBM for the implemented endpoints.

## 6. Machine learning plan — what to install and when

Do not install a large ML stack at launch. Separate LLM generation from predictive machine learning.

| Capability | Suggested technology | When to add |
|---|---|---|
| LLM ideation, analysis, script variants | Current OpenAI-compatible provider adapter | Already used by the AI service |
| Preference learning from Select/Reject/Watch | scikit-learn baseline; calibrated logistic regression or ranking model | After feature snapshots and sufficient reviewed examples exist |
| Tabular performance prediction | LightGBM or XGBoost, compared against simple baselines | After enough verified published outcomes and leakage-safe splits |
| Text/video semantic similarity | sentence-transformers embeddings + vector index | When the similarity engine is implemented and evaluated |
| Exact/near-duplicate frames | FFmpeg frame sampling + perceptual hashes/OpenCV | When authorized source media or permitted previews are available |
| Speech transcription/subtitles | Hosted speech API first; faster-whisper only if local processing is justified | When media pipeline is implemented |
| Experiment tracking/model registry | MLflow or equivalent | When models are trained, versioned and promoted |
| Monitoring/drift | Evidently or custom metrics and dashboards | After a stable production dataset exists |

Training rules:
- Store feature schema version, extractor version, observation time, source provenance and missingness.
- Split train/test by time and creator/account to avoid leakage.
- Keep Select, Reject and Watch distinct; Watch is uncertain, not a negative label.
- Keep creator preference labels separate from business performance outcomes.
- Use a human-approved champion/challenger rollout and rollback.
- Do not publish a numeric account-ban probability until there are verified enforcement outcomes, enough representative labels, calibration and documented uncertainty.

## 7. Configuration and secrets

Configure through a deployment secret manager or environment variables. Never commit production values into `appsettings.json`, `.env`, Docker images, browser JavaScript or logs.

### .NET API
- `ConnectionStrings__CreatorFlow`: SQL Server connection string.
- `Authentication__ApiKey`: temporary bootstrap service key (minimum 32 random characters). Replace with real user identity, roles, tenant isolation and per-user OAuth before public multi-tenant launch.
- `CREATORFLOW_AI_BASE_URL`: private base URL of the Python AI service.
- `CREATORFLOW_AI_API_KEY`: service-to-service secret; minimum 32 random characters.

### Blazor Web
- `CreatorFlowApi__BaseUrl`: internal HTTPS/API URL.
- `CreatorFlowApi__ApiKey`: server-side only; never expose in browser storage or client-side configuration.

### Python AI service
- `CREATORFLOW_AI_API_KEY`: must match the .NET service-to-service key.
- `CREATORFLOW_LLM_API_KEY`: provider credential.
- `CREATORFLOW_LLM_MODEL`: approved model name.
- Configure the provider base URL using the setting supported by the current LLM adapter.

### Platform OAuth (future connector configuration)
Keep separate credentials for TikTok and Douyin:
- Client key / app ID.
- Client secret.
- Redirect URI(s) registered exactly with the platform.
- Requested and approved scopes.
- Encrypted per-user access and refresh tokens, expiry and granted-scope records.
- Webhook/signature secrets if a provider integration requires them.

Use separate credentials for development, staging and production. Rotate secrets, restrict who can read them, and redact authorization headers/tokens from telemetry.

## 8. Data, storage and security

Before public launch, implement and verify:
- Real user authentication (OIDC/OAuth), MFA for administrators, RBAC and tenant/account isolation.
- Per-user platform connections and consent/revocation.
- SQL migrations as a controlled release step; least-privilege database identities.
- Encrypted object storage for media and source evidence, with signed short-lived URLs.
- Rights/provenance records, permitted-use scope, retention and deletion workflows.
- Rate limits, request-size limits, malware/content validation and SSRF protections for submitted URLs.
- Audit trail for user decisions, AI suggestions, source fetches and publishing actions.
- Backups, point-in-time recovery, restore drills, vulnerability scanning and dependency updates.
- Monitoring for API latency/errors, database health, queue depth, provider quota, token expiry, AI spend and storage growth.
- Privacy notice, terms, data-processing agreements where needed, user support and incident-response runbook.

Do not download or retain third-party videos merely because a public URL exists. Only ingest source media when the user or provider has granted rights and the applicable platform terms permit that use. For trend intelligence, store permitted metadata, aggregate measurements and evidence references rather than creating an unauthorized mirror of TikTok.

## 9. Public launch checklist

- [ ] Migrate .NET 9 projects and CI to .NET 10 LTS.
- [ ] Replace shared API key with user authentication, tenant isolation and authorization.
- [ ] Implement OAuth and account connection UX separately for TikTok and Douyin.
- [ ] Confirm app review, scope approval and platform UX requirements for each enabled action.
- [ ] Select a compliant Vietnam trend data source; document coverage, refresh interval, license and limitations.
- [ ] Implement durable job queue, idempotency, retry/backoff, dead-letter handling and job cancellation.
- [ ] Implement object storage, retention and rights evidence.
- [ ] Run SQL migration integration tests on clean and upgrade databases.
- [ ] Run load, security, backup/restore and failure-recovery tests.
- [ ] Configure dashboards, alerts, privacy/legal pages and support process.
- [ ] Gate every publishing action on explicit final user confirmation.

## 10. Suggested rollout

1. **Internal pilot:** Web/API + SQL + hosted LLM; manual trend sources and no automatic publishing.
2. **Connected-account pilot:** add TikTok/Douyin OAuth for accounts you own, only after platform app approval.
3. **Trend intelligence beta:** enable compliant data adapters with coverage labels, provenance, similarity and saturation.
4. **Production:** multi-tenant auth, durable workers, rights engine, tested operations, security review and official publishing/analytics.

The system must degrade honestly: when a provider is unavailable, show stale/partial coverage and do not invent market-wide saturation or account-risk percentages.
