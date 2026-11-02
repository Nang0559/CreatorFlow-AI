# Discovery feedback learning

## Product behavior
A recommendation is not considered correct merely because it has high views. Users can reject a video/channel and select a reason or add an explanation. CreatorFlow records the candidate, the ranking and feature snapshot at recommendation time, the decision, reason, topic/profile context and timestamp.

## Learning loop
1. Persist immutable feedback events in `DiscoveryFeedbackEvents`.
2. Preserve the original candidate feature snapshot in `DiscoveryCandidateFeatureSnapshots`.
3. Ask the LLM to interpret the reason into confirmed signals, uncertain hypotheses, related candidates to downrank and new discovery hints. The LLM must not invent facts.
4. Aggregate repeated feedback with recency decay and counter-evidence using `feedback_learning.py`.
5. Store proposed insights in `DiscoveryLearningInsights`; only approved profile/topic-scoped rules become active in `DiscoveryPreferenceRules`.
6. Apply bounded adjustments to ranking. Positive feedback can reverse a previous negative pattern.
7. Retrain an actual ML model only when there is enough labeled evidence and a time-based holdout evaluation. Record dataset/model versions and Precision@K, Recall@K, NDCG@K and calibration.

## Required safeguards
- One rejection never creates a permanent/global rule.
- A low-confidence LLM interpretation remains a hypothesis and is not activated automatically.
- User feedback is scoped to the user's profile and topic; do not leak preferences between users or tenants.
- Old feedback decays; newer positive feedback can counter older negative feedback.
- Do not treat missing engagement data as negative evidence.
- Do not use raw LLM prose as a ranking feature without validation.
- Do not train from one click or one video outcome; separate explicit preference feedback from observed performance.
- Preserve explainability: show which repeated reasons changed a recommendation and allow the user to correct/undo a preference.
- The current ranker remains a transparent heuristic baseline, not a trained ML model.

## API and persistence boundary
Migration 015 creates the feedback, feature snapshot, insight, preference-rule and model-version stores. The Python feedback aggregator and ranker are implemented and tested as pure components. The authenticated API persistence orchestration and .NET-to-SQL integration remain a required next implementation step; do not claim feedback is saved to SQL until that service path is wired and integration-tested.
