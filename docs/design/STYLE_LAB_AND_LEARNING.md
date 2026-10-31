# Style Lab and Style Learning — design

## 1. Objective
Use controlled creative experiments to learn a distinctive Vietnamese-market creator style from the creator's own measured outcomes. Douyin may inform topic and creative-mechanism discovery, but the engine must not clone a source creator's identity, dialogue or exact video execution.

## 2. Experiment contract
A StyleExperiment contains a required name (max 160 characters), testable hypothesis (max 2,000), optional content project, optional topic (max 200), and exactly two or three variants with distinct names. Every variant needs a name, hook, speaking style, edit pacing, audio approach and script.

### Lifecycle

~~~mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> Running: all variants Ready / Start
    Running --> Evaluated: all variants Measured / creator confirms winner
    Evaluated --> Archived: future archive operation
~~~

Variant lifecycle:

~~~mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> Ready
    Ready --> Published
    Published --> Measured
~~~

The domain enforces:
- Only Draft experiments start.
- Every variant must be Ready before Start.
- Only Ready variants can be Published.
- Only Published variants can receive metrics.
- Metrics cannot be negative; measurement cannot predate publication.
- Every variant must be Measured before evaluation.
- The winner must belong to the experiment and be Measured.
- The creator explicitly supplies the winner; engagement rate is not an automatic selector.

## 3. Measurement dimensions
A variant records views, likes, comments, shares, saves, follows gained, product clicks, orders, average watch seconds, video duration, PublishedAt and MeasuredAt.

Engagement rate excludes follows, clicks and orders. It is descriptive, not conversion rate or winner score. Compare equivalent measurement windows and consider the hypothesis. Winner selection is currently a creator decision, not a weighted KPI formula.

The current variant model stores one measurement snapshot in its metric fields. Repeated time-series snapshots for experiment variants are not modeled; Vietnam trend signals have their own snapshot table.

## 4. Creator style profile and evidence
CreatorStyleProfile stores an application-level ProfileKey, display name, positioning and editable StyleSummary.

The creator imports a completed experiment using POST /api/v1/style-profiles/{id}/learn/{experimentId}. Invariants:
- Experiment must be Evaluated with a confirmed winner.
- Winning variant must be Measured.
- Same experiment cannot be imported twice into one profile.
- Evidence snapshots the winner's creative dimensions and measured outcomes.
- Importing evidence does not rewrite StyleSummary.

Losing variants remain on the original experiment and are available to learning analysis.

## 5. Deterministic learning analysis — current implementation
StyleLearningEngine is a deterministic statistical summarizer, not a trained ML model and not an LLM judgment.

### Eligibility
The engine starts with Evaluated experiments having a non-null winner. A candidate is eligible when it has 2 or 3 variants, all variants are Measured, and every variant meets the minimum views threshold (default 100). API accepts 0 to 1,000,000,000; domain rejects negative values.

### Factors
The engine compares exact trimmed string values in four dimensions: Hook, SpeakingStyle, EditPacing and AudioApproach.

A factor is associated with an experiment when its exact value appears in a variant. It counts as a decisive win if it appears in the winner and no losing variant; decisive loss if it appears in a losing variant and not the winner. If the value appears in both winning and losing variants, that experiment is ambiguous for that factor and excluded from decisive support.

A signal is returned only with at least two decisive supporting experiments, at least two decisive wins, and positive lift over baseline.

### Baseline and lift
For each eligible experiment baseline is 1/N, where N is the number of variants. Overall baseline is the mean of those experiment baselines.

WinRate = decisive winning experiments / decisive supporting experiments.

Lift = WinRate - mean(1 / variants per eligible experiment).

This is descriptive association, not causal attribution. It does not prove a creative factor caused additional watch time, follows or orders. It does not weight factors by order value, retention, clicks or other business metrics in this iteration.

### Topic analysis
Experiments may carry a free-text topic. Analysis can filter by exact topic (case-insensitive) and also groups eligible experiments with non-empty topics. Historical NULL topics are not guessed or backfilled.

### API scope caveat
GET /api/v1/style-profiles/{profileId}/learning-analysis verifies that the profile exists, then analyzes the evaluated experiment pool. It does not filter by the profile's imported evidence or account key. Profile ID is a route existence check, not a data-isolation boundary. Implement profile/account-scoped evidence selection and tenant authorization before multi-account use.

The endpoint returns eligibility counts, signals, topic breakdown, a proposed summary and methodology. It does not persist a proposal, approval decision or profile version.

## 6. Creator review
The Style Profiles UI requests analysis, can place the proposed summary into the editable Style Summary draft, and leaves the creator to review and save guidance. This is a manual edit step, not a formal approval workflow: no persisted Pending/Approved/Rejected proposal or immutable profile version history exists yet.

## 7. LLM-assisted synthesis — implemented service boundary\n\nThe Python AI service now exposes a provider-backed evidence synthesis endpoint. It receives structured experiment evidence, profile positioning and current guidance; asks the configured LLM to summarize repeated strengths, conflicting evidence, uncertainty and next experiments; and validates that every cited experiment ID exists in the supplied evidence. The output is a review-required draft. This endpoint does not load evidence from SQL, enforce profile ownership, persist a proposal, or update a profile. Callers must supply a profile-scoped evidence set from a trusted application service; until that orchestration and tenant authorization exist, do not expose it as an account-isolated workflow.\n\nThe LLM is not permitted to turn correlation into causality or to promote a single experiment into a permanent rule. Deterministic eligibility/statistical analysis remains the auditable numeric layer; the LLM provides qualitative synthesis and experiment ideas.\n\n## 8. Next design increments
1. Persist StyleLearningProposal with source experiment IDs, evidence snapshot, factor details, topic scope, algorithm version, creation time and status.
2. Add Pending → Approved/Rejected, reviewer identity, review timestamp and reason.
3. Add immutable CreatorStyleProfileVersion; approval creates a new active version.
4. Scope analysis to profile/account evidence and enforce tenant authorization.
5. Define KPI policy and comparable measurement windows; retain views, retention, follows, clicks and orders as distinct measures.
6. Add optional LLM explanation over structured evidence, citing supporting experiments.
7. Feed only approved profile versions into script prompts and retain profile-version ID on generated scripts.
