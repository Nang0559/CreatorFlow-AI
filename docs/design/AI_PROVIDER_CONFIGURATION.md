# AI provider configuration

## Provider order

CreatorFlow AI uses a shared OpenAI-compatible Chat Completions adapter:
1. Gemini is primary.
2. OpenAI is the hosted fallback when its key is configured.
3. Ollama is optional and disabled until a local model is configured and explicitly added to the fallback list.

Discovery planning, source analysis, style variant generation and learning synthesis share this provider runtime.

## Environment

Configure these variables in the untracked local .env:
- CREATORFLOW_LLM_PRIMARY_PROVIDER=gemini
- CREATORFLOW_LLM_FALLBACK_PROVIDERS=openai
- CREATORFLOW_LLM_GEMINI_BASE_URL=https://generativelanguage.googleapis.com/v1beta/openai
- CREATORFLOW_LLM_GEMINI_API_KEY=your Gemini API key
- CREATORFLOW_LLM_GEMINI_MODEL=gemini-3.8-flash
- CREATORFLOW_LLM_OPENAI_BASE_URL=https://api.openai.com/v1
- CREATORFLOW_LLM_OPENAI_API_KEY=your OpenAI API key
- CREATORFLOW_LLM_OPENAI_MODEL=gpt-4.1-mini
- CREATORFLOW_LLM_OLLAMA_BASE_URL=http://host.docker.internal:11434/v1
- CREATORFLOW_LLM_OLLAMA_API_KEY=ollama
- CREATORFLOW_LLM_OLLAMA_MODEL=empty until a local model is installed
- CREATORFLOW_LLM_RETRIES_PER_PROVIDER=1
- CREATORFLOW_LLM_RETRY_BACKOFF_SECONDS=1

Old CREATORFLOW_LLM_API_KEY, CREATORFLOW_LLM_MODEL and CREATORFLOW_LLM_BASE_URL remain supported as Gemini aliases. Missing optional provider credentials/models cause that provider to be skipped. The primary provider must be configured.

## Retry and failover

Retry transient statuses 408, 409, 425, 429, 500, 502, 503 and 504, plus timeouts and network failures. Use bounded exponential backoff. After the primary retry budget is exhausted, try the next configured provider. Authentication/configuration errors are not retried. Invalid JSON advances to the next provider without repeatedly retrying the same one. If all providers fail, return an explicit 502; never fabricate a result.

## Optional Ollama on Windows

Ollama is not launched by Docker Compose. Install and run it on the Windows host, pull a model that fits the machine, set CREATORFLOW_LLM_OLLAMA_MODEL, then set CREATORFLOW_LLM_FALLBACK_PROVIDERS=openai,ollama. The AI container reaches Windows through host.docker.internal:11434. On machines with limited GPU memory, Ollama may run on CPU and be slower.

## Apply changes

Keep credentials in .env; never commit or share provider keys. After AI code/config changes, rebuild only AI:
docker compose --env-file .env -f compose.dev.yaml up -d --build ai
Invoke-RestMethod http://localhost:8001/ready

Readiness confirms configuration only, not remote key validity, quota or live availability.

## Boundaries

AI providers supply inference only. They do not discover Douyin/TikTok channels or supply sales evidence. Discovery requires an approved data provider and permitted access. Views and engagement are not proof of sales; sales claims require authorized commerce/affiliate evidence.
