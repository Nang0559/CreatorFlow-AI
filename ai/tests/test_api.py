from __future__ import annotations

from typing import Any

from fastapi.testclient import TestClient

from creatorflow_ai.main import app, get_llm_client

client = TestClient(app)


class FakeLLM:
    def __init__(self, result: dict[str, Any]):
        self.result = result
        self.calls: list[dict[str, Any]] = []

    async def complete_json(self, *, system_prompt: str, user_payload: dict[str, Any]) -> dict[str, Any]:
        self.calls.append({"system_prompt": system_prompt, "user_payload": user_payload})
        return self.result


def auth(monkeypatch) -> dict[str, str]:
    monkeypatch.setenv("CREATORFLOW_AI_API_KEY", "test-service-key-0123456789abcdef")
    return {"X-CreatorFlow-AI-Key": "test-service-key-0123456789abcdef"}


def test_health_is_public() -> None:
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "healthy"


def test_ready_reports_missing_model_configuration(monkeypatch) -> None:
    monkeypatch.delenv("CREATORFLOW_LLM_API_KEY", raising=False)
    monkeypatch.delenv("CREATORFLOW_LLM_MODEL", raising=False)
    response = client.get("/ready")
    assert response.status_code == 200
    assert response.json()["ready"] is False


def test_ai_routes_require_service_authentication(monkeypatch) -> None:
    monkeypatch.setenv("CREATORFLOW_AI_API_KEY", "test-service-key-0123456789abcdef")
    response = client.post("/v1/analyze", json={"text": "Một ý tưởng"})
    assert response.status_code == 401


def test_analyze_calls_llm_and_requires_review(monkeypatch) -> None:
    headers = auth(monkeypatch)
    fake = FakeLLM({
        "language": "vi", "summary": "Mở đầu bằng một vấn đề quen thuộc.",
        "topics": ["đồ gia dụng"], "audience_intent": "Tìm giải pháp tiết kiệm thời gian",
        "story_mechanisms": ["vấn đề - giải pháp - kết quả"],
        "hook_patterns": ["câu hỏi gợi tò mò"],
        "localization_opportunities": ["dùng bối cảnh căn hộ Việt Nam"],
        "elements_to_avoid_copying": ["câu thoại và trình tự cảnh đặc trưng"],
    })
    app.dependency_overrides[get_llm_client] = lambda: fake
    try:
        response = client.post("/v1/analyze", headers=headers, json={"text": "Reference transcript", "topic": "đồ gia dụng", "creatorPositioning": "reviewer thực tế"})
    finally:
        app.dependency_overrides.clear()
    assert response.status_code == 200
    assert response.json()["requiresHumanReview"] is True
    assert len(fake.calls) == 1


def test_variant_generation_enforces_requested_count(monkeypatch) -> None:
    headers = auth(monkeypatch)
    fake = FakeLLM({
        "topic": "đồ gia dụng",
        "variants": [
            {"name": f"Phương án {i}", "creative_angle": "Góc kể khác", "hook": "Bạn có gặp vấn đề này?",
             "speaking_style": "Gần gũi", "edit_pacing": "Nhanh", "audio_approach": "Voice-over",
             "script": "Kịch bản tiếng Việt.", "shot_plan": ["Cảnh 1"], "call_to_action": "Xem thông tin sản phẩm."}
            for i in range(1, 4)
        ],
        "differentiation_notes": ["Khác nhau về góc tiếp cận"],
    })
    app.dependency_overrides[get_llm_client] = lambda: fake
    try:
        response = client.post("/v1/style-lab/generate-variants", headers=headers, json={
            "sourceAnalysis": "Audience wants a faster routine", "topic": "đồ gia dụng",
            "creatorPositioning": "Người dùng thử sản phẩm thực tế", "variantCount": 3,
        })
    finally:
        app.dependency_overrides.clear()
    assert response.status_code == 200
    assert len(response.json()["variants"]) == 3
    assert response.json()["requiresHumanReview"] is True


def test_variant_generation_rejects_wrong_count(monkeypatch) -> None:
    headers = auth(monkeypatch)
    fake = FakeLLM({"topic": "test", "variants": [], "differentiation_notes": []})
    app.dependency_overrides[get_llm_client] = lambda: fake
    try:
        response = client.post("/v1/style-lab/generate-variants", headers=headers, json={
            "sourceAnalysis": "analysis", "topic": "test", "creatorPositioning": "creator",
            "variantCount": 2,
        })
    finally:
        app.dependency_overrides.clear()
    assert response.status_code == 502


def test_learning_synthesis_rejects_fabricated_experiment_citations(monkeypatch) -> None:
    headers = auth(monkeypatch)
    fake = FakeLLM({
        "proposed_style_summary": "Tóm tắt", "recurring_strengths": [], "conflicting_evidence": [],
        "next_experiments": [], "cited_experiment_ids": ["not-in-evidence"], "uncertainty": "Mẫu còn nhỏ.",
    })
    app.dependency_overrides[get_llm_client] = lambda: fake
    try:
        response = client.post("/v1/learning/synthesize", headers=headers, json={
            "profile_key": "creator-vn", "creator_positioning": "Đánh giá sản phẩm",
            "evidence": [
                {"experiment_id": "exp-1", "winning_variant_id": "v-1"},
                {"experiment_id": "exp-2", "winning_variant_id": "v-2"},
            ],
        })
    finally:
        app.dependency_overrides.clear()
    assert response.status_code == 502


def test_analyze_rejects_empty_text(monkeypatch) -> None:
    monkeypatch.setenv("CREATORFLOW_AI_API_KEY", "test-service-key-0123456789abcdef")
    monkeypatch.setenv("CREATORFLOW_LLM_API_KEY", "test-provider-key")
    monkeypatch.setenv("CREATORFLOW_LLM_MODEL", "test-model")
    response = client.post("/v1/analyze", headers={"X-CreatorFlow-AI-Key": "test-service-key-0123456789abcdef"}, json={"text": ""})
    assert response.status_code == 422

def test_analyze_reports_missing_provider_after_validating_request(monkeypatch) -> None:
    monkeypatch.setenv("CREATORFLOW_AI_API_KEY", "test-service-key-0123456789abcdef")
    for name in (
        "CREATORFLOW_LLM_API_KEY", "CREATORFLOW_LLM_MODEL", "CREATORFLOW_LLM_BASE_URL",
        "CREATORFLOW_LLM_GEMINI_API_KEY", "CREATORFLOW_LLM_GEMINI_MODEL",
        "CREATORFLOW_LLM_OPENAI_API_KEY", "CREATORFLOW_LLM_OPENAI_MODEL",
        "CREATORFLOW_LLM_OLLAMA_MODEL",
    ):
        monkeypatch.delenv(name, raising=False)

    response = client.post(
        "/v1/analyze",
        headers={"X-CreatorFlow-AI-Key": "test-service-key-0123456789abcdef"},
        json={"text": "Nội dung hợp lệ"},
    )
    assert response.status_code == 503
    assert response.json()["detail"] == "AI provider configuration is invalid or unavailable."


def test_discovery_rank_works_without_shared_link_and_priority_is_bounded(monkeypatch) -> None:
    headers = auth(monkeypatch)
    base = {"topicFit": 90, "viewsPerHour": 1200, "engagementRate": 0.08,
            "marketFit": 85, "creatorQuality": 75, "dataCoverage": 0.8}
    ordinary = client.post("/v1/discovery/rank", headers=headers, json=base)
    prioritized = client.post("/v1/discovery/rank", headers=headers, json={**base, "prioritySignal": True})
    assert ordinary.status_code == prioritized.status_code == 200
    assert ordinary.json()["priorityBoost"] == 0
    assert prioritized.json()["priorityBoost"] == 20
    assert prioritized.json()["score"] >= ordinary.json()["score"]
    assert ordinary.json()["explanation"]["limitations"].startswith("Heuristic baseline")


def test_discovery_rank_rejects_out_of_range_features(monkeypatch) -> None:
    headers = auth(monkeypatch)
    response = client.post("/v1/discovery/rank", headers=headers, json={"topicFit": 120})
    assert response.status_code == 422


def test_discovery_query_plan_is_optional_link_independent_and_bounded(monkeypatch) -> None:
    headers = auth(monkeypatch)
    fake = FakeLLM({
        "topic": "đồ gia dụng",
        "queries": ["đồ gia dụng thông minh", "mẹo nhà bếp tiết kiệm thời gian"],
        "related_topics": ["tổ chức nhà cửa"],
        "intent_signals": ["giải quyết vấn đề thường ngày"],
        "exclusions": ["nội dung quảng cáo không có bằng chứng"],
    })
    app.dependency_overrides[get_llm_client] = lambda: fake
    try:
        response = client.post("/v1/discovery/query-plan", headers=headers, json={
            "topic": "đồ gia dụng", "targetMarket": "Vietnam", "maxQueries": 2
        })
    finally:
        app.dependency_overrides.clear()
    assert response.status_code == 200
    assert len(response.json()["queries"]) == 2
    assert response.json()["requiresHumanReview"] is True
    assert "shared_url" not in fake.calls[0]["user_payload"]
