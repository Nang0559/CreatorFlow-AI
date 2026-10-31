# AI Style Learning Engine

## Purpose

Turn measured 2–3-way creative experiments into explainable style signals. The engine compares confirmed winners with losing variants across experiments, then separates signals by content topic. It does not copy source-creator identity or automatically rewrite a creator's profile.

## Evidence gates

An experiment is eligible only when:
- its status is `Evaluated` and it has a confirmed winning variant;
- it contains 2 or 3 variants;
- every variant has a measured metrics snapshot;
- every variant meets the configurable minimum-view threshold (default 100).

A factor is surfaced only after it appears in at least two distinct eligible experiments, wins at least twice, and its win rate exceeds the experiment-level random-choice baseline. The first factors are hook, speaking style, edit pacing and audio approach.

## Topic attribution

Every new experiment can optionally carry a topic (max 200 characters). Analysis can be scoped to one topic and also returns topic-level breakdowns. Historical experiments with no topic remain in the global analysis but are not assigned to a fabricated topic.

## Interpretation and safety

- The current engine is deterministic and explainable; it is not presented as a trained ML model or LLM conclusion.
- The baseline is the average of 1/N variants per eligible experiment. Factor lift is its observed win rate minus that baseline.
- The analysis is associative, not causal. It does not claim a hook caused an increase in orders.
- The API returns a proposed summary only. A creator must review it before any profile guidance is changed.
- Insufficient sample sizes are excluded rather than treated as failures.
- This first iteration does not yet persist review decisions, generate AI-written explanations, or feed the proposal automatically into script prompts. Those are subsequent workflow increments.

## API

`GET /api/v1/style-profiles/{profileId}/learning-analysis?topic=...&minimumViewsPerVariant=100`

The endpoint returns eligibility counts, repeated factor signals, topic breakdown, a proposed style summary, and the methodology. It is a read-only preview; existing profile guidance and evidence are unchanged.

## Database

Apply `database/schema/007_style_learning_topic.sql` after schema scripts 001–006. Existing rows receive a NULL topic and are not retroactively classified.
