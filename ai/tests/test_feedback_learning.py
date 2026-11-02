from creatorflow_ai.feedback_learning import FeedbackEvent, summarize_feedback


def test_single_negative_feedback_does_not_create_permanent_rule() -> None:
    result = summarize_feedback([
        FeedbackEvent("NotRelevant", "irrelevant", "topic_fit", "beauty", 0)
    ])
    assert result["rules"] == []
    assert result["single_feedback_creates_rule"] is False


def test_repeated_negative_feedback_creates_reviewable_scoped_penalty() -> None:
    result = summarize_feedback([
        FeedbackEvent("NotRelevant", "irrelevant", "topic_fit", "unrelated_gadget", 0),
        FeedbackEvent("Hide", "irrelevant", "topic_fit", "unrelated_gadget", 2),
        FeedbackEvent("NotRelevant", "irrelevant", "topic_fit", "unrelated_gadget", 5),
    ])
    assert len(result["rules"]) == 1
    rule = result["rules"][0]
    assert rule["effect"] == "Penalty"
    assert rule["scope"] == "profile_topic"
    assert rule["requires_review"] is True


def test_positive_feedback_can_counter_negative_feedback() -> None:
    events = [
        FeedbackEvent("NotRelevant", "bad_match", "creator_type", "viral_only", 0),
        FeedbackEvent("NotRelevant", "bad_match", "creator_type", "viral_only", 1),
        FeedbackEvent("Interested", "good_match", "creator_type", "viral_only", 2),
        FeedbackEvent("Followed", "good_match", "creator_type", "viral_only", 3),
    ]
    result = summarize_feedback(events)
    assert result["rules"] == [] or result["rules"][0]["effect"] == "Boost"


def test_feedback_weight_decays_with_age() -> None:
    recent = summarize_feedback([
        FeedbackEvent("NotRelevant", "bad", "topic", "x", 0),
        FeedbackEvent("Hide", "bad", "topic", "x", 1),
        FeedbackEvent("NotRelevant", "bad", "topic", "x", 2),
    ])
    old = summarize_feedback([
        FeedbackEvent("NotRelevant", "bad", "topic", "x", 900),
        FeedbackEvent("Hide", "bad", "topic", "x", 901),
        FeedbackEvent("NotRelevant", "bad", "topic", "x", 902),
    ])
    assert recent["rules"][0]["weight"] > old["rules"][0]["weight"] if old["rules"] else True
