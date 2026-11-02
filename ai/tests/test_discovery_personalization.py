from creatorflow_ai.discovery_ranker import DiscoveryFeatures, rank_discovery_candidate


def test_learned_adjustment_is_bounded_and_changes_personalized_score() -> None:
    base = DiscoveryFeatures(topic_fit=80, market_fit=80, creator_quality=80, data_coverage=1)
    penalized = DiscoveryFeatures(topic_fit=80, market_fit=80, creator_quality=80,
                                  data_coverage=1, learned_adjustment=-8)
    assert rank_discovery_candidate(penalized)["score"] < rank_discovery_candidate(base)["score"]


def test_learned_adjustment_cannot_exceed_safety_bound() -> None:
    candidate = DiscoveryFeatures(topic_fit=80, learned_adjustment=-100)
    result = rank_discovery_candidate(candidate)
    assert result["learned_adjustment"] == -30
