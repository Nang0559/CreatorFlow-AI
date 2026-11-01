# Getting started

## Prerequisites
- .NET 9 SDK (current branch baseline only; migrate to .NET 10 LTS before public launch)
- Python 3.11+
- SQL Server 2022+ (optional for API health endpoint)
- FFmpeg only when media processing is introduced

## .NET
```bash
dotnet restore CreatorFlow.sln
dotnet build CreatorFlow.sln --configuration Release
dotnet run --project src/CreatorFlow.Api
```

The API exposes `/` and `/health`. Set `ConnectionStrings__CreatorFlow` to configure SQL Server. The bootstrap API does not claim persistence is fully wired.

## Python AI service
```bash
python -m venv .venv
# Windows: .venv\\Scripts\\activate
# macOS/Linux: source .venv/bin/activate
pip install -e ai
uvicorn creatorflow_ai.main:app --app-dir ai --reload --port 8001
```

Never commit API keys, database passwords, publishing tokens or licensed media.

## Deployment and public launch

The selected deployment target is SaaS multi-tenant on hybrid infrastructure, with separate TikTok Vietnam and Douyin China integrations. See:
- [SaaS + Hybrid Architecture](SAAS_HYBRID_ARCHITECTURE.md)
- [Deployment Guide](DEPLOYMENT_GUIDE.md)
- [Deployment Runbook](DEPLOYMENT_RUNBOOK.md)
- [Public Launch Checklist](PUBLIC_LAUNCH_CHECKLIST.md)

The current repository is a pilot foundation, not yet a public multi-tenant SaaS release. The shared API key is bootstrap service authentication, not customer identity. Do not connect customer accounts until tenant isolation and per-tenant OAuth are implemented.

## Local creator workspace

Run the API with SQL Server and a server-side bootstrap API key, then configure the Web project to call it:

```bash
# API process
ConnectionStrings__CreatorFlow="<sql-server-connection-string>"
Authentication__ApiKey="<at-least-32-character-secret>"
dotnet run --project src/CreatorFlow.Api

# Web process (server-side only; never put the API key in browser storage)
CreatorFlowApi__BaseUrl="https://localhost:7001"
CreatorFlowApi__ApiKey="<same-api-secret>"
dotnet run --project src/CreatorFlow.Web
```

The API URL must match the actual local API address/certificate. On Windows PowerShell, set these values with `$env:NAME="value"` before starting each process. For production, use a deployment secret manager.

For real LLM calls, configure `CREATORFLOW_AI_BASE_URL` and `CREATORFLOW_AI_API_KEY` on the .NET API, plus `CREATORFLOW_AI_API_KEY`, `CREATORFLOW_LLM_API_KEY` and `CREATORFLOW_LLM_MODEL` on the Python service. Use separate internal service and provider keys; never expose either to the browser.

## SQL setup

Apply all numbered SQL scripts in order (001–010) to the dedicated CreatorFlow database, following the migration-specific notes in this repository. Do not apply to an unrelated or production database without backup and review. Migration 010 SQL Server execution tests against both an empty database and schema-009 upgrade database remain an outstanding release gate.

## Design documentation

Start at [docs/design/README.md](design/README.md) for the as-built system overview, data model, Style Lab/Style Learning design and implementation gaps.
