"""Evidence aggregation for profile-scoped discovery preferences.

This module deliberately does not train a model from a single thumbs-down.
Persist events and generated insights in SQL migration 015; activate rules only
after repeated evidence and review by the application policy layer.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from math import exp


@dataclass(frozen=True)
class FeedbackEvent:
    decision: str
    reason_code: str
    feature_key: str | None = None
    feature_value: str | None = None
    created_days_ago: float = 0.0


def summarize_feedback(events: list[FeedbackEvent], *, minimum_evidence: int = 3) -> dict[str, object]:
    """Summarize repeated, recent feedback; positive evidence can counter negative evidence."""
    if minimum_evidence < 2:
        raise ValueError("minimum_evidence must be at least 2")
    counts: Counter[tuple[str, str]] = Counter()
    weighted: Counter[tuple[str, str]] = Counter()
    for event in events:
        if event.decision not in {"Interested", "NotRelevant", "Save", "Hide", "Followed", "Converted"}:
            continue
        if not event.feature_key or event.feature_value is None:
            continue
        key = (event.feature_key, event.feature_value)
        counts[key] += 1
        recency = exp(-max(0.0, event.created_days_ago) / 90.0)
        sign = -1.0 if event.decision in {"NotRelevant", "Hide"} else 1.0
        strength = 1.5 if event.decision in {"Followed", "Converted"} else 1.0
        weighted[key] += sign * strength * recency

    rules: list[dict[str, object]] = []
    for (feature_key, feature_value), count in counts.items():
        net = weighted[(feature_key, feature_value)]
        if count < minimum_evidence or abs(net) < 1.5:
            continue
        rules.append({
            "feature_key": feature_key,
            "feature_value": feature_value,
            "effect": "Penalty" if net < 0 else "Boost",
            "weight": round(min(10.0, abs(net) * 1.5), 3),
            "evidence_count": count,
            "confidence": round(min(0.95, count / (count + 3) * min(1.0, abs(net) / count)), 4),
            "requires_review": True,
            "scope": "profile_topic",
        })
    return {
        "rules": sorted(rules, key=lambda item: float(item["weight"]), reverse=True),
        "events_considered": len(events),
        "minimum_evidence": minimum_evidence,
        "single_feedback_creates_rule": False,
        "positive_feedback_can_reverse_negative": True,
        "recency_decay_days": 90,
    }
