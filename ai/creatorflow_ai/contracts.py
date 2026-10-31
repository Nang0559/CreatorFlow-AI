from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field, field_validator\n\n\ndef to_camel(value: str) -> str:\n    first, *rest = value.split("_")\n    return first + "".join(part[:1].upper() + part[1:] for part in rest)\n\n\nclass CreatorFlowModel(BaseModel):\n    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class AnalyzeRequest(BaseModel):
    text: str = Field(min_length=1, max_length=20_000)
    language: str = Field(default="vi", min_length=2, max_length=12)
    topic: str | None = Field(default=None, max_length=200)
    creator_positioning: str | None = Field(default=None, max_length=2_000)


class AnalyzeResponse(BaseModel):
    language: str
    summary: str
    topics: list[str]
    audience_intent: str
    story_mechanisms: list[str]
    hook_patterns: list[str]
    localization_opportunities: list[str]
    elements_to_avoid_copying: list[str]
    requires_human_review: bool = True


class GenerateVariantsRequest(BaseModel):
    source_analysis: str = Field(min_length=1, max_length=12_000)
    topic: str = Field(min_length=1, max_length=200)
    creator_positioning: str = Field(min_length=1, max_length=2_000)
    style_summary: str = Field(default="", max_length=5_000)
    product_context: str | None = Field(default=None, max_length=4_000)
    variant_count: int = Field(default=3, ge=2, le=3)
    target_duration_seconds: int = Field(default=45, ge=10, le=180)


class GeneratedVariant(BaseModel):
    name: str = Field(min_length=1, max_length=100)
    creative_angle: str = Field(min_length=1, max_length=500)
    hook: str = Field(min_length=1, max_length=500)
    speaking_style: str = Field(min_length=1, max_length=300)
    edit_pacing: str = Field(min_length=1, max_length=300)
    audio_approach: str = Field(min_length=1, max_length=300)
    script: str = Field(min_length=1, max_length=12_000)
    shot_plan: list[str] = Field(min_length=1, max_length=20)
    call_to_action: str = Field(min_length=1, max_length=500)


class GenerateVariantsResponse(BaseModel):
    topic: str
    language: str = "vi"
    variants: list[GeneratedVariant] = Field(min_length=2, max_length=3)
    differentiation_notes: list[str]
    requires_human_review: bool = True

    @field_validator("variants")
    @classmethod
    def variants_must_be_distinct(cls, variants: list[GeneratedVariant]) -> list[GeneratedVariant]:
        names = [item.name.strip().casefold() for item in variants]
        if len(names) != len(set(names)):
            raise ValueError("Variant names must be unique.")
        return variants


class LearningEvidence(BaseModel):
    experiment_id: str = Field(min_length=1, max_length=100)
    topic: str | None = Field(default=None, max_length=200)
    winning_variant_id: str = Field(min_length=1, max_length=100)
    winning_factors: list[str] = Field(default_factory=list, max_length=30)
    losing_factors: list[str] = Field(default_factory=list, max_length=30)
    metrics: dict[str, float] = Field(default_factory=dict)


class SynthesizeLearningRequest(BaseModel):
    profile_key: str = Field(min_length=1, max_length=200)
    creator_positioning: str = Field(min_length=1, max_length=2_000)
    current_style_summary: str = Field(default="", max_length=5_000)
    evidence: list[LearningEvidence] = Field(min_length=2, max_length=100)


class LearningSynthesisResponse(BaseModel):
    proposed_style_summary: str = Field(min_length=1, max_length=5_000)
    recurring_strengths: list[str] = Field(max_length=20)
    conflicting_evidence: list[str] = Field(max_length=20)
    next_experiments: list[str] = Field(max_length=10)
    cited_experiment_ids: list[str] = Field(max_length=100)
    uncertainty: str = Field(min_length=1, max_length=2_000)
    requires_human_review: bool = True
