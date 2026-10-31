from __future__ import annotations

import asyncio
import json

import httpx
import pytest

from creatorflow_ai.llm import (
    AIConfigurationError,
    AIInvalidOutputError,
    LLMSettings,
    OpenAICompatibleLLM,
)


def test_settings_require_provider_key_and_model(monkeypatch) -> None:
    monkeypatch.delenv("CREATORFLOW_LLM_API_KEY", raising=False)
    monkeypatch.delenv("CREATORFLOW_LLM_MODEL", raising=False)
    with pytest.raises(AIConfigurationError):
        LLMSettings.from_environment()


def test_settings_reject_insecure_remote_provider_url(monkeypatch) -> None:
    monkeypatch.setenv("CREATORFLOW_LLM_API_KEY", "secret")
    monkeypatch.setenv("CREATORFLOW_LLM_MODEL", "model-name")
    monkeypatch.setenv("CREATORFLOW_LLM_BASE_URL", "http://remote.example/v1")
    with pytest.raises(AIConfigurationError):
        LLMSettings.from_environment()


def test_openai_compatible_adapter_sends_json_mode_and_parses_response() -> None:
    seen: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["authorization"] = request.headers.get("Authorization")
        seen["body"] = json.loads(request.content)
        return httpx.Response(200, json={
            "choices": [{"message": {"content": json.dumps({"summary": "structured output"})}}]
        })

    settings = LLMSettings("https://provider.example/v1", "server-secret", "test-model", 5, 512)
    llm = OpenAICompatibleLLM(settings, transport=httpx.MockTransport(handler))
    result = asyncio.run(llm.complete_json(
        system_prompt="Return JSON only.",
        user_payload={"source_text": "reference"},
    ))

    assert result == {"summary": "structured output"}
    assert seen["url"] == "https://provider.example/v1/chat/completions"
    assert seen["authorization"] == "Bearer server-secret"
    assert seen["body"]["response_format"] == {"type": "json_object"}
    assert seen["body"]["model"] == "test-model"


def test_openai_compatible_adapter_rejects_non_json_content() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={
            "choices": [{"message": {"content": "not-json"}}]
        })

    settings = LLMSettings("https://provider.example/v1", "server-secret", "test-model", 5, 512)
    llm = OpenAICompatibleLLM(settings, transport=httpx.MockTransport(handler))
    with pytest.raises(AIInvalidOutputError):
        asyncio.run(llm.complete_json(system_prompt="Return JSON.", user_payload={}))
