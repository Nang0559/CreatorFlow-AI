# Getting started

## Prerequisites
- .NET 9 SDK (current branch baseline only; migrate to .NET 10 LTS before public launch)
- Python 3.11+
- SQL Server 2022+ (optional for API health endpoint)
- PowerShell 5.1+ and the SqlServer PowerShell module for database setup
- After Effects (licensed), `aerender.exe`, FFmpeg and FFprobe when using the local video-render workflow

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
# Windows: .venv\Scripts\activate
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

The repository now contains the baseline OIDC login and tenant membership lifecycle, but is not yet a public multi-tenant SaaS release. Configure a real OIDC provider and complete the remaining release gates before inviting external customers. The shared API key is Development-only bootstrap service authentication, not customer identity. See [OIDC identity setup](IDENTITY_SETUP.md).

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

## SQL database setup

The repository now includes a guarded SQL Server initializer. It creates only the local `CreatorFlow_QC` database, applies numbered migrations 001–010 and 014–020 (plus 011–013 when explicitly selected), records SHA-256 checksums in `dbo.SchemaMigrations`, and verifies the resulting schema.

Install the SQL Server PowerShell module once from an elevated PowerShell session:

```powershell
Install-Module SqlServer -Scope AllUsers -Force -AllowClobber
```

From the repository root, initialize the complete Trend Intelligence + tenant schema:

```powershell
.\database\Initialize-CreatorFlowDatabase.ps1 -SqlInstance localhost -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment
```

The tenant flag applies migrations 011–013. Migration 012 assigns existing rows in its listed single-workspace tables to the explicit `legacy-workspace` tenant; review that assumption before using it on a database containing multiple customers. Migration 013 adds invitation and security-audit tables. Migrations 014–020 add Discovery, share inbox, video rendering and tenant relationship/uniqueness safeguards. The initializer refuses to adopt an existing untagged database or a database with user tables but no migration ledger. It never drops or restores a database.

To initialize only the original baseline schema (001–010), omit both tenant switches and use this only for legacy compatibility checks; the current CreatorFlow application requires later migrations for its newer features:

```powershell
.\database\Initialize-CreatorFlowDatabase.ps1 -SqlInstance localhost
```

The QC runner uses a non-sysadmin Windows identity. Run initialization separately as a SQL administrator; CI never creates databases or runs migrations. See [SQL database setup](SQL_DATABASE_SETUP.md) and [Windows SQL Server QC runner](SQLSERVER_QC_RUNNER.md).

## Design documentation

Start at [docs/design/README.md](design/README.md) for the as-built system overview, data model, Style Lab/Style Learning design and implementation gaps.
