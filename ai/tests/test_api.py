from fastapi.testclient import TestClient
from creatorflow_ai.main import app

client = TestClient(app)


def test_health() -> None:
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "healthy"


def test_analyze_requires_human_review() -> None:
    response = client.post("/v1/analyze", json={"text": "A creator idea", "language": "en"})
    assert response.status_code == 200
    assert response.json()["requires_human_review"] is True


def test_analyze_rejects_empty_text() -> None:
    response = client.post("/v1/analyze", json={"text": ""})
    assert response.status_code == 422
