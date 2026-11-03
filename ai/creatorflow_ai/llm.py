"""Multi-provider OpenAI-compatible runtime: Gemini primary, OpenAI fallback, optional Ollama."""
from __future__ import annotations
import asyncio, json, os
from dataclasses import dataclass, field
from typing import Any
from urllib.parse import urlparse
import httpx

_RETRYABLE={408,409,425,429,500,502,503,504}

class AIConfigurationError(RuntimeError): pass
class AIInvalidOutputError(RuntimeError): pass
class AIProviderError(RuntimeError):
    def __init__(self,message:str,*,provider:str="unknown",retryable:bool=False):
        super().__init__(message); self.provider=provider; self.retryable=retryable

@dataclass(frozen=True)
class ProviderConfig:
    name:str
    base_url:str
    api_key:str
    model:str

@dataclass(frozen=True)
class LLMSettings:
    base_url:str
    api_key:str
    model:str
    timeout_seconds:float=60.0
    max_output_tokens:int=3000
    provider:str="gemini"
    fallbacks:tuple[ProviderConfig,...]=field(default_factory=tuple)
    retries_per_provider:int=1
    retry_backoff_seconds:float=1.0

    @classmethod
    def from_environment(cls)->"LLMSettings":
        try:
            timeout=float(os.getenv("CREATORFLOW_LLM_TIMEOUT_SECONDS","60"))
            tokens=int(os.getenv("CREATORFLOW_LLM_MAX_OUTPUT_TOKENS","3000"))
            retries=int(os.getenv("CREATORFLOW_LLM_RETRIES_PER_PROVIDER","1"))
            backoff=float(os.getenv("CREATORFLOW_LLM_RETRY_BACKOFF_SECONDS","1"))
        except ValueError as exc: raise AIConfigurationError("LLM timeout, token and retry settings must be numeric.") from exc
        if timeout<=0 or not 256<=tokens<=16000 or not 0<=retries<=3 or not 0<=backoff<=10:
            raise AIConfigurationError("Invalid LLM limits: timeout > 0, tokens 256..16000, retries 0..3, backoff 0..10.")
        legacy_key=os.getenv("CREATORFLOW_LLM_API_KEY","").strip()
        legacy_model=os.getenv("CREATORFLOW_LLM_MODEL","").strip()
        legacy_url=os.getenv("CREATORFLOW_LLM_BASE_URL","").strip()
        configs={
          "gemini":(os.getenv("CREATORFLOW_LLM_GEMINI_BASE_URL","").strip() or legacy_url or "https://generativelanguage.googleapis.com/v1beta/openai",
                    os.getenv("CREATORFLOW_LLM_GEMINI_API_KEY","").strip() or legacy_key,
                    os.getenv("CREATORFLOW_LLM_GEMINI_MODEL","").strip() or legacy_model or "gemini-3.8-flash"),
          "openai":(os.getenv("CREATORFLOW_LLM_OPENAI_BASE_URL","").strip() or "https://api.openai.com/v1",
                    os.getenv("CREATORFLOW_LLM_OPENAI_API_KEY","").strip(),
                    os.getenv("CREATORFLOW_LLM_OPENAI_MODEL","").strip() or "gpt-4.1-mini"),
          "ollama":(os.getenv("CREATORFLOW_LLM_OLLAMA_BASE_URL","").strip() or "http://host.docker.internal:11434/v1",
                    os.getenv("CREATORFLOW_LLM_OLLAMA_API_KEY","").strip() or "ollama",
                    os.getenv("CREATORFLOW_LLM_OLLAMA_MODEL","").strip())}
        primary=os.getenv("CREATORFLOW_LLM_PRIMARY_PROVIDER","gemini").strip().lower()
        if primary not in configs: raise AIConfigurationError("Primary provider must be gemini, openai or ollama.")
        names=[v.strip().lower() for v in os.getenv("CREATORFLOW_LLM_FALLBACK_PROVIDERS","openai").split(",") if v.strip()]
        if any(v not in configs for v in names): raise AIConfigurationError("Fallback list contains an unsupported provider.")
        def validate(name,base):
            p=urlparse(base); local={"localhost","127.0.0.1","::1"}
            if name=="ollama": local.add("host.docker.internal")
            if p.scheme!="https" and not (p.scheme=="http" and p.hostname in local):
                raise AIConfigurationError("Provider URLs must use HTTPS; HTTP is allowed only for local Ollama.")
            if not p.hostname or p.username or p.password or p.query or p.fragment:
                raise AIConfigurationError("Provider base URL is invalid.")
        base,key,model=configs[primary]
        if not key or not model: raise AIConfigurationError(f"Primary provider '{primary}' requires an API key and model.")
        validate(primary,base)
        fallbacks=[]
        for name in names:
            if name==primary: continue
            b,k,m=configs[name]
            if not k or not m: continue
            validate(name,b); fallbacks.append(ProviderConfig(name,b.rstrip("/"),k,m))
        return cls(base.rstrip("/"),key,model,timeout,tokens,primary,tuple(fallbacks),retries,backoff)

class OpenAICompatibleLLM:
    def __init__(self,settings:LLMSettings,transport:httpx.AsyncBaseTransport|None=None,
                 transports:dict[str,httpx.AsyncBaseTransport]|None=None):
        self._settings=settings; self._transport=transport; self._transports=transports or {}

    async def _complete_one(self,name,base,key,model,transport,system_prompt,user_payload):
        body={"model":model,"temperature":0.4,"max_tokens":self._settings.max_output_tokens,
              "response_format":{"type":"json_object"},
              "messages":[{"role":"system","content":system_prompt},
                          {"role":"user","content":json.dumps(user_payload,ensure_ascii=False)}]}
        try:
            timeout=httpx.Timeout(self._settings.timeout_seconds,connect=min(20,self._settings.timeout_seconds))
            async with httpx.AsyncClient(timeout=timeout,transport=transport) as client:
                response=await client.post(base.rstrip("/")+"/chat/completions",
                    headers={"Authorization":f"Bearer {key}","Content-Type":"application/json"},json=body)
                response.raise_for_status()
                content=response.json()["choices"][0]["message"]["content"]
                try: result=json.loads(content)
                except json.JSONDecodeError as exc: raise AIInvalidOutputError("Model response is not valid JSON.") from exc
                if not isinstance(result,dict): raise AIInvalidOutputError("Model response must be a JSON object.")
                return result
        except AIInvalidOutputError: raise
        except httpx.HTTPStatusError as exc:
            status=exc.response.status_code
            raise AIProviderError(f"AI provider '{name}' returned HTTP {status}.",provider=name,retryable=status in _RETRYABLE) from exc
        except (httpx.TimeoutException,httpx.NetworkError) as exc:
            raise AIProviderError(f"AI provider '{name}' timed out or is unreachable.",provider=name,retryable=True) from exc
        except (httpx.HTTPError,KeyError,IndexError,TypeError,ValueError) as exc:
            raise AIProviderError(f"AI provider '{name}' failed or returned an invalid response.",provider=name) from exc

    async def complete_json(self,*,system_prompt:str,user_payload:dict[str,Any])->dict[str,Any]:
        routes=[(self._settings.provider,self._settings.base_url,self._settings.api_key,self._settings.model,
                 self._transport or self._transports.get(self._settings.provider))]
        routes += [(p.name,p.base_url,p.api_key,p.model,self._transports.get(p.name)) for p in self._settings.fallbacks]
        last_error=None
        for name,base,key,model,transport in routes:
            for attempt in range(self._settings.retries_per_provider+1):
                try:
                    return await self._complete_one(name,base,key,model,transport,system_prompt,user_payload)
                except AIInvalidOutputError as exc:
                    last_error=exc; break
                except AIProviderError as exc:
                    last_error=exc
                    if not exc.retryable or attempt>=self._settings.retries_per_provider: break
                    await asyncio.sleep(min(self._settings.retry_backoff_seconds*(2**attempt),8))
        if last_error is None: raise AIConfigurationError("No AI provider is configured.")
        raise last_error
