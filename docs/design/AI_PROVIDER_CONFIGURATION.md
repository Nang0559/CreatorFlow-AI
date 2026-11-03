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

## Workspace-managed provider registry

Migration `024_ai_provider_registry.sql` creates the tenant-scoped `dbo.AIProviders` registry. Owner/Admin users manage providers at `/settings/ai-providers`. The API returns metadata and a `HasApiKey` flag only; it never returns the plaintext key or its ciphertext. An empty API-key field during update preserves the existing credential.

The API encrypts credentials with ASP.NET Core Data Protection. The key ring is persisted outside the repository under `%LOCALAPPDATA%\\CreatorFlow\\DataProtectionKeys` for the Windows account running the API, unless `CREATORFLOW_DATA_PROTECTION_KEYS_PATH` is explicitly configured. Back up this directory securely; losing it makes stored provider credentials undecryptable. Do not copy it into Git, Docker images or shared logs.

When a workspace has an active Primary provider, the API decrypts its route and sends it to the AI service only over the authenticated internal API-to-AI channel. The AI service uses that route for the current request and applies the configured fallbacks. If no active workspace Primary exists, the existing environment-based Gemini/OpenAI/Ollama configuration remains the fallback. The browser never receives provider credentials.

The provider test calls the OpenAI-compatible `/models` endpoint. A successful test confirms endpoint authentication/availability at that moment; it does not guarantee inference quota or that every model operation is supported.

## Apply on the Z240

1. Pull the latest `feature/topic-driven-channel-discovery` branch.
2. Apply tenant migration `024_ai_provider_registry.sql` with the existing migration runner and its tenant-migration confirmation safeguards. Do not use `down -v`.
3. Start the API using the same Windows account each time so the persistent Data Protection key ring remains accessible. If you choose a custom key-ring directory, set `CREATORFLOW_DATA_PROTECTION_KEYS_PATH` before starting the API and restrict its NTFS permissions to that account.
4. Rebuild the Web and AI containers after pulling. The API runs natively on Windows and must be restarted to load the new code.
5. Sign in as workspace Owner/Admin and open `/settings/ai-providers`. Add Gemini as active Primary. Add OpenAI as Fallback only after its API billing/key is ready. Add Ollama only if a local model is installed and reachable from the AI container at `host.docker.internal`.
6. Test each provider, then run one real discovery-plan or analysis request. Provider test alone does not prove inference success.

Keep the environment-based Gemini configuration in place during initial rollout so there is a fallback if the workspace registry is empty. Never paste credentials into chat, source files, migration scripts or logs.

## Boundaries

AI providers supply inference only. They do not discover Douyin/TikTok channels or supply sales evidence. Discovery requires an approved data provider and permitted access. Views and engagement are not proof of sales; sales claims require authorized commerce/affiliate evidence.
