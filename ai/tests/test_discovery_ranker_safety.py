from math import isfinite, nan, inf

from creatorflow_ai.discovery_ranker import DiscoveryFeatures, rank_discovery_candidate


def test_ranker_clamps_non_finite_feature_values() -> None:
    result = rank_discovery_candidate(
        DiscoveryFeatures(
            topic_fit=nan,
            views_per_hour=inf,
            engagement_rate=nan,
            market_fit=nan,
            creator_quality=inf,
            data_coverage=nan,
            learned_adjustment=inf,
        )
    )

    for key in ("score", "topic_fit", "momentum", "market_fit", "creator_quality", "confidence"):
        assert isfinite(float(result[key]))
    assert 0 <= result["score"] <= 100
    assert 0 <= result["confidence"] <= 1


def test_negative_view_velocity_does_not_produce_negative_confidence() -> None:
    negative = rank_discovery_candidate(DiscoveryFeatures(topic_fit=70, views_per_hour=-500))
    zero = rank_discovery_candidate(DiscoveryFeatures(topic_fit=70, views_per_hour=0))

    assert negative["momentum"] == zero["momentum"]
    assert negative["confidence"] == zero["confidence"]
    assert 0 <= negative["confidence"] <= 1
