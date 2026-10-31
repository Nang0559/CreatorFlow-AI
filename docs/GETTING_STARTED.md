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
