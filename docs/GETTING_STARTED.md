# Getting started

## Prerequisites
- .NET 9 SDK
- Python 3.11+
- SQL Server 2022+ (optional for API health endpoint)
- FFmpeg (when media processing is introduced)

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

Never commit API keys, database passwords, publishing tokens, or licensed media.


## Creator workspace (Blazor)

Run the API with SQL Server and a server-side API key, then configure the Web project to call it:

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

The API URL must match the actual local API address/certificate. On Windows PowerShell, set these values with `$env:NAME="value"` before starting each process. For production, store the API key in the deployment secret manager rather than a committed settings file.

For real LLM calls, configure `CREATORFLOW_AI_BASE_URL` and `CREATORFLOW_AI_API_KEY` on the .NET API, plus `CREATORFLOW_AI_API_KEY`, `CREATORFLOW_LLM_API_KEY` and `CREATORFLOW_LLM_MODEL` on the Python service. Use a separate internal service key and provider key; never expose either to the browser.\n\nThe interactive Web workspace includes:
- `/trends`: manually record Vietnam creator/video/sound trend observations.
- `/style-lab`: create 2–3 style variants, mark them ready/published, enter performance metrics and evaluate a winner.
- `/style-profiles`: create a creator style profile, add evaluated winners as evidence and maintain an editable style summary.

Apply SQL scripts in numeric order to the dedicated CreatorFlow database. The Vietnam trend/style workflow uses scripts `005_vietnam_trends_and_style_lab.sql`, `006_creator_style_profiles.sql`, `007_style_learning_topic.sql` and `008_style_variant_metric_observations.sql` after the earlier foundation scripts. Apply all numbered scripts in order (001–008) to the dedicated CreatorFlow database. Migration 008 is required for immutable Style Lab metric history; do not deploy the new measurement API before applying it.


## Design documentation

Start at [docs/design/README.md](design/README.md) for the as-built system overview, data model, Style Lab/Style Learning design and implementation gaps. These documents distinguish implemented workflows from future platform connectors and production capabilities.
