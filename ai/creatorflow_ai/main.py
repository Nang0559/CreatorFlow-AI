from fastapi import FastAPI
from pydantic import BaseModel, Field

app = FastAPI(title="CreatorFlow AI", version="0.1.0")


class AnalyzeRequest(BaseModel):
    text: str = Field(min_length=1, max_length=20_000)
    language: str = Field(default="vi", min_length=2, max_length=12)


@app.get("/health")
def health() -> dict[str, str]:
    return {"service": "creatorflow-ai", "status": "healthy", "version": "0.1.0"}


@app.post("/v1/analyze")
def analyze(request: AnalyzeRequest) -> dict[str, object]:
    """Baseline contract. Model providers will be injected behind an adapter."""
    normalized = " ".join(request.text.split())
    return {
        "status": "draft",
        "language": request.language,
        "character_count": len(normalized),
        "summary": None,
        "topics": [],
        "requires_human_review": True,
    }
