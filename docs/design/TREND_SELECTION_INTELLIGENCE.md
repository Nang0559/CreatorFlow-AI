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
4. Feature extraction runs separately and writes a versioned feature snapshot; until implemented the snapshot remains {}.
5. Preference analysis compares Select and Reject; Watch is excluded from binary labels.
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
POST and GET /api/v1/douyin/videos/{id}/feedback, plus POST and GET /api/v1/douyin/videos/{id}/feedback/{feedbackId}/outcomes. FeatureSnapshotJson remains {} until a real extractor exists. Existing shared API-key auth is bootstrap-only; verified user identity and tenant ownership remain production blockers.

## Release gates
Migration tests on empty and schema-009 databases; decision validation and empty-note round-trip; append-only history; preference labels exclude Watch; connector compliance and coverage; rights review; calibrated risk model; human approval and official publishing API.
