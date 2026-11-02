"""Transparent, versioned baseline ranker. This is heuristic ranking baseline, not a trained model."""
from __future__ import annotations

from dataclasses import dataclass
from math import isfinite


ALGORITHM_VERSION = "heuristic-discovery-v1"


@dataclass(frozen=True)
class DiscoveryFeatures:
    topic_fit: float
    views_per_hour: float = 0.0
    engagement_rate: float = 0.0
    market_fit: float = 50.0
    creator_quality: float = 50.0
    data_coverage: float = 0.5
    priority_signal: bool = False
    learned_adjustment: float = 0.0


def _clamp(value: float, low: float = 0.0, high: float = 100.0) -> float:
    if not isfinite(value):
        return low
    return max(low, min(high, value))


def _scale(value: float, ceiling: float) -> float:
    return _clamp(value / ceiling * 100.0)


def rank_discovery_candidate(features: DiscoveryFeatures) -> dict[str, object]:
    """Rank without requiring a user-submitted URL; priority is an optional bounded boost."""
    topic = _clamp(features.topic_fit)
    momentum = _clamp(0.65 * _scale(max(0.0, features.views_per_hour), 5000.0)
                      + 0.35 * _scale(max(0.0, features.engagement_rate), 0.15))
    market = _clamp(features.market_fit)
    creator = _clamp(features.creator_quality)
    coverage = _clamp(features.data_coverage, 0.0, 1.0)
    priority_boost = 20.0 if features.priority_signal else 0.0
    base = 0.38 * topic + 0.27 * momentum + 0.20 * market + 0.15 * creator
    learned_adjustment = _clamp(features.learned_adjustment, -30.0, 30.0)
    score = round(_clamp(base + priority_boost + learned_adjustment), 4)
    views_per_hour = max(0.0, features.views_per_hour) if isfinite(features.views_per_hour) else 0.0
    confidence = round(coverage * min(1.0, 0.35 + 0.65 * (0.5 + min(0.5, views_per_hour / 10000.0))), 4)
    return {
        "algorithm_version": ALGORITHM_VERSION,
        "score": score,
        "topic_fit": round(topic, 4),
        "momentum": round(momentum, 4),
        "market_fit": round(market, 4),
        "creator_quality": round(creator, 4),
        "priority_boost": priority_boost,
        "learned_adjustment": round(learned_adjustment, 4),
        "confidence": confidence,
        "explanation": {
            "weights": {"topic_fit": 0.38, "momentum": 0.27, "market_fit": 0.20, "creator_quality": 0.15},
            "priority_is_optional": True,
            "learned_adjustment_is_bounded": True,
            "priority_signal_applied": features.priority_signal,
            "data_coverage": round(coverage, 4),
            "limitations": "Heuristic baseline; not a trained or calibrated predictive model."
        },
    }
