# AI provider configuration

## Default provider

CreatorFlow AI uses Google Gemini API by default through Google's official OpenAI-compatible Chat Completions endpoint.

- Base URL: `https://generativelanguage.googleapis.com/v1beta/openai`
- Starter model: `gemini-3.8-flash`
- API key: create one in [Google AI Studio](https://aistudio.google.com/apikey)
- Environment variables: `CREATORFLOW_LLM_BASE_URL`, `CREATORFLOW_LLM_API_KEY`, `CREATORFLOW_LLM_MODEL`

The Python runtime sends Chat Completions requests and requests JSON-object output. Gemini documents OpenAI compatibility and structured JSON output. Keep the provider key in the local `.env`; never commit it, paste it into issues, or send it in chat.

## Local setup

Add or update these entries in the untracked local `.env` file:

```dotenv
CREATORFLOW_LLM_BASE_URL=https://generativelanguage.googleapis.com/v1beta/openai
CREATORFLOW_LLM_API_KEY=YOUR_GEMINI_API_KEY
CREATORFLOW_LLM_MODEL=gemini-3.8-flash
CREATORFLOW_LLM_TIMEOUT_SECONDS=60
CREATORFLOW_LLM_MAX_OUTPUT_TOKENS=3000
```

Rebuild only the AI service:

```powershell
docker compose --env-file .env -f compose.dev.yaml up -d --build ai
Invoke-RestMethod http://localhost:8001/ready
```

Readiness should report `ready = True`, `llm_configured = True`, and `model = gemini-3.8-flash`. This confirms configuration only; a real authenticated feature request is still needed to verify provider credentials, quota and structured output.

## Switching providers later

The AI runtime is deliberately provider-neutral at the Chat Completions boundary. To switch to another provider that supports the same protocol, change the following three settings together:

1. `CREATORFLOW_LLM_BASE_URL`: provider's documented OpenAI-compatible base URL.
2. `CREATORFLOW_LLM_API_KEY`: credential for that provider.
3. `CREATORFLOW_LLM_MODEL`: a model supported by that endpoint.

Then rebuild/recreate the AI service. The API routes and feature prompts do not need provider-specific changes. For a provider that does not implement OpenAI-compatible Chat Completions or JSON-object output, add and test a dedicated adapter behind the same `complete_json` interface rather than adding provider logic to feature endpoints.

## Boundaries

- Gemini supplies model inference; it does not discover Douyin/TikTok channels or provide sales data by itself.
- Discovery still needs an approved data provider and permitted access. Views and engagement are not proof of sales.
- Google API quotas, billing, data handling terms and model availability apply. Check the current Google AI Studio/API documentation before production rollout.
- `/ready` checks local configuration, not whether the remote key is valid or whether the account has quota.
