from __future__ import annotations
import asyncio
import httpx
import pytest
from creatorflow_ai.llm import AIConfigurationError,AIInvalidOutputError,AIProviderError,LLMSettings,OpenAICompatibleLLM,ProviderConfig

def test_settings_require_provider_key_and_model(monkeypatch):
    monkeypatch.delenv("CREATORFLOW_LLM_API_KEY",raising=False); monkeypatch.delenv("CREATORFLOW_LLM_MODEL",raising=False)
    with pytest.raises(AIConfigurationError): LLMSettings.from_environment()

def test_settings_reject_insecure_remote_provider_url(monkeypatch):
    monkeypatch.setenv("CREATORFLOW_LLM_API_KEY","secret"); monkeypatch.setenv("CREATORFLOW_LLM_MODEL","model")
    monkeypatch.setenv("CREATORFLOW_LLM_BASE_URL","http://remote.example/v1")
    with pytest.raises(AIConfigurationError): LLMSettings.from_environment()

def test_gemini_compatible_endpoint_parses_json():
    seen={}
    def handler(r):
        seen["url"]=str(r.url); seen["auth"]=r.headers.get("Authorization")
        return httpx.Response(200,json={"choices":[{"message":{"content":'{"summary":"ok"}'}}]})
    s=LLMSettings("https://generativelanguage.googleapis.com/v1beta/openai","g-key","gemini-3.8-flash",5,512)
    result=asyncio.run(OpenAICompatibleLLM(s,httpx.MockTransport(handler)).complete_json(system_prompt="JSON",user_payload={}))
    assert result=={"summary":"ok"} and seen["url"].endswith("/chat/completions") and seen["auth"]=="Bearer g-key"

def test_invalid_json_is_rejected():
    s=LLMSettings("https://provider.example/v1","secret","model",5,512)
    llm=OpenAICompatibleLLM(s,httpx.MockTransport(lambda r:httpx.Response(200,json={"choices":[{"message":{"content":"bad"}}]})))
    with pytest.raises(AIInvalidOutputError): asyncio.run(llm.complete_json(system_prompt="JSON",user_payload={}))

def test_router_falls_back_after_gemini_503():
    calls={"gemini":0,"openai":0}
    def gemini(r): calls["gemini"]+=1; return httpx.Response(503)
    def openai(r):
        calls["openai"]+=1
        return httpx.Response(200,json={"choices":[{"message":{"content":'{"provider":"openai"}'}}]})
    s=LLMSettings("https://g.example/v1","g","gm",2,512,"gemini",
      (ProviderConfig("openai","https://o.example/v1","o","om"),),0,0)
    result=asyncio.run(OpenAICompatibleLLM(s,transports={"gemini":httpx.MockTransport(gemini),"openai":httpx.MockTransport(openai)}).complete_json(system_prompt="JSON",user_payload={}))
    assert result=={"provider":"openai"} and calls=={"gemini":1,"openai":1}

def test_primary_retries_before_fallback():
    calls={"gemini":0,"openai":0}
    def gemini(r):
        calls["gemini"]+=1
        if calls["gemini"]==1:return httpx.Response(503)
        return httpx.Response(200,json={"choices":[{"message":{"content":'{"provider":"gemini"}'}}]})
    def openai(r): calls["openai"]+=1; return httpx.Response(200,json={"choices":[{"message":{"content":'{"provider":"openai"}'}}]})
    s=LLMSettings("https://g.example/v1","g","gm",2,512,"gemini",
      (ProviderConfig("openai","https://o.example/v1","o","om"),),1,0)
    result=asyncio.run(OpenAICompatibleLLM(s,transports={"gemini":httpx.MockTransport(gemini),"openai":httpx.MockTransport(openai)}).complete_json(system_prompt="JSON",user_payload={}))
    assert result=={"provider":"gemini"} and calls=={"gemini":2,"openai":0}

def test_local_ollama_http_is_allowed_but_remote_http_is_not(monkeypatch):
    monkeypatch.setenv("CREATORFLOW_LLM_API_KEY","g"); monkeypatch.setenv("CREATORFLOW_LLM_MODEL","m")
    monkeypatch.setenv("CREATORFLOW_LLM_PRIMARY_PROVIDER","ollama")
    monkeypatch.setenv("CREATORFLOW_LLM_OLLAMA_MODEL","local-model")
    monkeypatch.setenv("CREATORFLOW_LLM_OLLAMA_BASE_URL","http://host.docker.internal:11434/v1")
    assert LLMSettings.from_environment().provider=="ollama"


def test_all_provider_failures_report_primary_and_fallback_errors():
    def gemini(r): return httpx.Response(503)
    def openai(r): return httpx.Response(429)
    settings=LLMSettings("https://g.example/v1","g","gm",2,512,"gemini",
      (ProviderConfig("openai","https://o.example/v1","o","om"),),0,0)
    llm=OpenAICompatibleLLM(settings,transports={
      "gemini":httpx.MockTransport(gemini),
      "openai":httpx.MockTransport(openai)})
    with pytest.raises(AIProviderError) as caught:
        asyncio.run(llm.complete_json(system_prompt="JSON",user_payload={}))
    message=str(caught.value)
    assert "gemini: AI provider 'gemini' returned HTTP 503." in message
    assert "openai: AI provider 'openai' returned HTTP 429." in message
