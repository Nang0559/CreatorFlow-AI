"""Provider-neutral runtime for OpenAI-compatible chat-completions APIs.

The runtime intentionally has no local/mock fallback: missing credentials or a
model configuration is an explicit service-unavailable condition.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Any

import httpx


class AIConfigurationError(RuntimeError):
    pass


class AIProviderError(RuntimeError):
    pass


class AIInvalidOutputError(RuntimeError):
    pass


@dataclass(frozen=True)
class LLMSettings:
    base_url: str
    api_key: str
    model: str
    timeout_seconds: float = 60.0
    max_output_tokens: int = 3000

    @classmethod
    def from_environment(cls) -> "LLMSettings":
        api_key = os.getenv("CREATORFLOW_LLM_API_KEY", "").strip()
        model = os.getenv("CREATORFLOW_LLM_MODEL", "").strip()
        if not api_key or not model:
            raise AIConfigurationError(
                "CREATORFLOW_LLM_API_KEY and CREATORFLOW_LLM_MODEL must be configured."
            )
        base_url = os.getenv("CREATORFLOW_LLM_BASE_URL", "https://api.openai.com/v1").strip().rstrip("/")
        if not base_url.startswith("https://") and not base_url.startswith("http://localhost"):
            raise AIConfigurationError("CREATORFLOW_LLM_BASE_URL must use HTTPS (HTTP is allowed only for localhost).")
        try:
            timeout = float(os.getenv("CREATORFLOW_LLM_TIMEOUT_SECONDS", "60"))
            max_tokens = int(os.getenv("CREATORFLOW_LLM_MAX_OUTPUT_TOKENS", "3000"))
        except ValueError as exc:
            raise AIConfigurationError("LLM timeout and output token settings must be numeric.") from exc
        if timeout <= 0 or max_tokens < 256 or max_tokens > 16000:
            raise AIConfigurationError("LLM timeout must be positive and output tokens must be between 256 and 16000.")
        return cls(base_url, api_key, model, timeout, max_tokens)


class OpenAICompatibleLLM:
    def __init__(self, settings: LLMSettings, transport: httpx.AsyncBaseTransport | None = None):
        self._settings = settings
        self._transport = transport

    async def complete_json(self, *, system_prompt: str, user_payload: dict[str, Any]) -> dict[str, Any]:
        body = {
            "model": self._settings.model,
            "temperature": 0.4,
            "max_tokens": self._settings.max_output_tokens,
            "response_format": {"type": "json_object"},
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": json.dumps(user_payload, ensure_ascii=False)},
            ],
        }
        try:
            async with httpx.AsyncClient(timeout=self._settings.timeout_seconds, transport=self._transport) as client:
                response = await client.post(
                    f"{self._settings.base_url}/chat/completions",
                    headers={"Authorization": f"Bearer {self._settings.api_key}", "Content-Type": "application/json"},
                    json=body,
                )
                response.raise_for_status()
                payload = response.json()
                content = payload["choices"][0]["message"]["content"]
                parsed = json.loads(content)
                if not isinstance(parsed, dict):
                    raise AIInvalidOutputError("The model response must be a JSON object.")
                return parsed
        except AIInvalidOutputError:
            raise
        except (httpx.HTTPError, KeyError, IndexError, TypeError, ValueError, json.JSONDecodeError) as exc:
            raise AIProviderError("The configured LLM provider failed or returned an invalid response.") from exc
