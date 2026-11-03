# Trend Selection, Saturation & Publishing Safety Intelligence

## Purpose and invariants
CreatorFlow learns the creator's selection preferences, measures market saturation and assesses publishing safety while optimizing time from discovery to approved original content.
- Creator Select/Reject/Watch is the source label for preference learning; it is not proof of market success.
- Watch is deferred/uncertain, never a negative label.
- LLM may describe features but cannot rewrite creator decisions.
- Market outcomes are recorded separately from selection.
- Saturation is competitive pressure, not proof of infringement.
- No numerical account-ban probability until a model is trained and calibrated on verified outcomes.
- No unauthorized scraping, downloading or publishing bypass. Use permitted APIs/providers, user-supplied links and manual observations with source provenance.

## Creator feedback workflow
1. Creator chooses Select, Reject or Watch and a reason code such as Hook, Story, Novelty, VietnamDemand, LowSaturation, CommercialPotential, Format, Sound or Other.
2. Optional note may be empty; example decision=Select, reasonCode=Hook, reasonNote="".
3. API captures immutable candidate metadata and appends the decision.
4. The API extracts and persists versioned metadata-only feature snapshot schema v1 (discovery method, source/topic presence, title shape, publication age and discovery age); it excludes creator decision and mutable candidate status to avoid label leakage.
5. End-to-end preference analysis is not yet connected to this SQL feedback stream. When implemented, Select/Reject may form the binary decision labels; Watch must remain deferred/uncertain and excluded from binary labels.
6. Adaptation, publication and measured market results are appended as separate outcome observations.

## Implemented in migration 010
TrendSelectionFeedback stores candidate FK, decision, reason code/note, candidate and feature JSON snapshots, and review time. It is append-only and indexed for candidate history and decision/reason analysis. TrendCandidateOutcomeObservations stores downstream outcome kind, value, metric snapshot, source and observation time. The API supports feedback create/history and outcome observation create/history. SQL triggers enforce append-only behavior for both tables; domain validation rejects undefined enum values and non-object/invalid JSON snapshots.

## TikTok Vietnam scanning design
Planned entities: TikTokContentScanJobs, TikTokContentMatches, TikTokContentMetricSnapshots, TikTokSaturationAssessments, ContentRightsAssessments and TikTokPublishingRiskAssessments.
Each observation records provider/source, run ID, observed time, scope, freshness, coverage and permitted use. A connector must be approved and rate-limited. TikTok Research API access is eligibility-gated; do not assume it is available to a Vietnam commercial creator application.
Do not scrape/crawl TikTok or download third-party media unless expressly permitted. Manual URL intake and authorized providers are supported fallback modes.

## Saturation and similarity
Calculate separate 24h, 7d and 30d windows: distinct matching videos/accounts, new matches, velocity change, engagement distribution and data coverage. Never extrapolate a partial sample to platform-wide totals without showing coverage.
Similarity is staged: exact IDs/URLs; hashes for user-provided media; perceptual frames; authorized audio fingerprint; transcript/semantic similarity; human review. A match is evidence, not an infringement verdict.

## Risk dimensions and calibration
Display separately: content removal risk, recommendation eligibility risk and account enforcement risk. Show Low/Medium/High/Unknown, evidence, source freshness, model version and confidence.
Do not display a percentage until a representative verified labeled dataset exists, with temporal holdout validation, calibration metrics, subgroup checks and drift monitoring. Missing account history or rights evidence means Unknown/Needs review, never an invented probability. Saturation alone must not increase account enforcement risk.

## Speed architecture
List-first paginated dashboard; indexed queries; creator feedback writes stay synchronous and lightweight. Scans, feature extraction, similarity and AI work run in durable idempotent jobs with bounded concurrency, rate limits, retries, dead-letter handling, progress checkpoints and source provenance.
Proposed, unmeasured SLO targets: dashboard P95 <2s; feedback write P95 <500ms; status visibility <1s.

## API currently implemented
POST and GET /api/v1/douyin/videos/{id}/feedback, plus POST and GET /api/v1/douyin/videos/{id}/feedback/{feedbackId}/outcomes. FeatureSnapshotJson stores the deterministic schema-v1 metadata snapshot. OIDC bearer identity and tenant membership foundations exist; the shared API key is Development-only. Production OIDC configuration, cross-tenant negative tests and tenant-scoping of the separate Discovery SQL tables remain blockers.

## Release gates
Migration tests on empty and schema-009 databases; decision validation and empty-note round-trip; append-only history; preference labels exclude Watch; connector compliance and coverage; rights review; calibrated risk model; human approval and official publishing API.


## Autonomous discovery implementation (migration 014 + AI runtime)
- Discovery does not require a user-shared URL. Topic profiles drive scheduled discovery plans against enabled, permitted public/official providers.
- A shared URL is an optional priority signal. The baseline ranker caps the boost at 20/100; it cannot replace topic fit, momentum, Vietnam-market fit or creator-quality evidence.
- SQL migration 014 persists discovery runs, topic profiles, provider provenance/access mode, candidates, metric snapshots with coverage, versioned rankings, optional priority links, feedback and model-evaluation records.
- AI endpoints:
  - `POST /v1/discovery/rank`: deterministic, low-latency heuristic baseline. Algorithm version is `heuristic-discovery-v1`; it is not represented as a trained ML model.
  - `POST /v1/discovery/query-plan`: LLM proposes bounded search queries, adjacent topics, intent signals and exclusions. It does not collect data or assert that a trend exists.
- Rank inputs are bounded and confidence is reduced when source coverage is incomplete. All ranking output includes component scores, weights, version and limitations.
- A trained model is a later gate: collect consented/legitimate creator feedback and outcome labels, create temporal train/validation splits, evaluate Precision@K/Recall@K/NDCG@K and calibration, then register the model and dataset version. Do not train on Watch as a negative label.
- Douyin policy: no device login, login QR, cookies, session extraction, fingerprint spoofing or account interaction. Douyin authorized-read integration remains disabled until a separately approved official permission model is verified; phone-only use and manual/public permitted sources remain the safe path.
- TikTok/Douyin discovery providers must be individually reviewed for terms, coverage, rate limits and allowed use. Disabled providers are not queried. No generic scraper is an acceptable fallback.
- Tenant note: migration 014 extends the existing single-workspace discovery data model. Do not expose its new tables to multi-tenant users until a tenant-scoping migration and ownership tests are added.


## Current Discovery ranking boundary

The Python service exposes a transparent heuristic candidate ranker and an LLM query planner. The ranker can score candidates supplied to it without a user-shared link; a shared URL is only an optional bounded priority signal. The query planner returns permitted search queries and exclusions but does not collect data. No verified live Douyin/TikTok provider, candidate ingestion worker, persistent ranking orchestration or feedback-to-next-ranking loop is active. Do not describe the current baseline as trained ML or automatic live trend crawling.


## Topic-driven channel discovery — implementation status

The Trends page now offers a topic-driven discovery entry point:
- The creator selects an active saved topic and may add product and audience context.
- The authenticated API reads the topic from the current tenant, derives a bounded keyword list, and forwards a query-plan request to the AI service.
- The AI service returns bounded queries, adjacent topics, purchase-intent signals and exclusions. Human review remains required.
- This is a query plan only. It does not claim that channels were fetched, does not follow accounts, and does not create fabricated channel metrics.

### Provider and commercial evidence gates

Automatic channel acquisition is intentionally not enabled until a provider adapter is approved and configured. A provider must declare its platform, permitted search/profile/video capabilities, authorization model, rate limits, geographic coverage, retention rules and allowed commercial use. Disabled or unconfigured providers must fail closed and must never be replaced by browser-cookie extraction, private endpoints or generic scraping.

Commercial ranking must keep separate evidence classes:
1. Verified sales evidence: orders, units sold, GMV or affiliate conversion metrics returned by an authorized seller/affiliate integration, with the authorization scope and reporting window recorded.
2. Creator-authorized performance evidence: creator profile/video/shop metrics available only after that creator authorizes the application.
3. Public content evidence: views, likes, comments, shares, posting cadence and engagement velocity. These can indicate audience traction but are not proof of sales.
4. Unknown: absent or unauthorized metrics. Unknown is not zero and cannot increase a commercial-sales score.

TikTok Shop Affiliate API is approval-gated; Seller Analytics APIs are scoped to the authorized seller's shop and require the relevant scopes. Douyin Open Platform permissions are separately applied for and may be disabled by default. Therefore neither platform currently provides a universal, unrestricted cross-creator sales lookup for this application.

### Remaining release gates

- Implement and test an approved provider adapter for topic search and public/authorized channel discovery.
- Persist discovery runs, candidate source provenance, coverage, timestamps and tenant ownership using a tenant-safe schema.
- Deduplicate candidates by platform + stable external creator ID and preserve source URLs.
- Add channel metric snapshots with nullable values and explicit source/authorization provenance.
- Rank commercial potential only from verified sales evidence; expose public-content traction as a separate score.
- Add scheduled worker execution, bounded retries, rate limiting, idempotency and a human review queue.
- Verify API/Web builds, tenant isolation, migration checksums and connector contract tests before enabling automatic collection.
