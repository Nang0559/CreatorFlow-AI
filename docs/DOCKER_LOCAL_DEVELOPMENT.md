# CreatorFlow-AI — Local Docker Pilot (Windows + SQL Server)

## 1. Selected topology (Option A)

- Web: Blazor Server in Docker, published only on 127.0.0.1:8080.
- AI: Python FastAPI in Docker, published only on 127.0.0.1:8001.
- API: .NET API runs natively on Windows at port 7001.
- Database: existing local SQL Server instance and CreatorFlow_QC, using Windows Authentication from the Windows API process.
- API → AI: http://localhost:8001; the API client permits HTTP only for loopback.
- Web → API: http://host.docker.internal:7001; the API key is sent server-to-server by Blazor Server and is not browser configuration.

This keeps SQL Server out of Docker and does not require Mixed Mode or a SQL login. Compose does not publish SQL Server or expose the AI service beyond host loopback.

## 2. Prerequisites

- Windows 11, Docker Desktop using Linux containers, WSL2.
- .NET SDK 9.0.100 or later in the 9.0 feature band.
- SQL Server running and the QC database already initialized with the repository migration scripts.
- A valid OpenAI-compatible LLM endpoint, API key and model for actual AI generation. The AI container can start without LLM credentials, but /ready reports not ready and generation requests return a configuration error.

## 3. Prepare local secrets

From the repository root:

    Copy-Item .env.example .env
    notepad .env

Set these values in .env (never commit this file):

- Authentication__ApiKey: random secret of at least 32 characters.
- CreatorFlowApi__ApiKey: exactly the same value as Authentication__ApiKey.
- CREATORFLOW_AI_API_KEY: a separate random secret of at least 32 characters; shared only by the Windows API and AI container.
- ConnectionStrings__CreatorFlow: local Windows-authenticated SQL connection string, for example:

    Server=localhost;Database=CreatorFlow_QC;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;

- CREATORFLOW_LLM_API_KEY and CREATORFLOW_LLM_MODEL: set these to the credentials and model supported by your chosen OpenAI-compatible provider. Keep the provider key private. If a secret contains a dollar sign, wrap the .env value in single quotes.

Use a password manager or a local secret generator to create keys. Do not paste secrets into chat, source files, screenshots or Git commits.

## 4. Start Web and AI containers

In PowerShell, from the repository root:

    docker compose --env-file .env -f compose.dev.yaml config --quiet
    docker compose --env-file .env -f compose.dev.yaml up --build -d
    docker compose --env-file .env -f compose.dev.yaml ps
    Invoke-RestMethod http://localhost:8001/health
    Invoke-RestMethod http://localhost:8001/ready

Expected: AI /health returns status=healthy. /ready is true only after LLM credentials and model are configured.

Open Web at http://localhost:8080.

## 5. Start the Windows API

Open a second PowerShell window, change to the repository root, then run:

    . .\scripts\docker\Import-LocalPilotEnv.ps1
    Import-LocalPilotEnv
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = "http://0.0.0.0:7001"
    $env:Tenant__AllowApiKeyBootstrap = "true"
    $env:Tenant__BootstrapTenantId = "00000000-0000-4000-8000-000000000001"
    $env:CREATORFLOW_AI_BASE_URL = "http://localhost:8001"
    dotnet run --project src/CreatorFlow.Api/CreatorFlow.Api.csproj --no-launch-profile

The API process inherits the current Windows user's identity. Run it as the Windows account that has the intended access to CreatorFlow_QC. The example connection string uses Integrated Security and does not create or require a SQL password.

Verify from another PowerShell window:

    Invoke-RestMethod http://localhost:7001/health
    Invoke-RestMethod http://localhost:7001/

The API must be running before using Web features that call it.

## 6. Network safety

The API listens on all host interfaces so the Docker Desktop Web container can reach it through host.docker.internal. Keep Windows Defender Firewall enabled. Do not create a broad inbound allow rule for port 7001, do not forward ports on the router, and do not expose ports 7001, 8001 or SQL Server to the public Internet. Compose binds Web and AI published ports to loopback only.

If Web cannot reach the API, inspect Docker Desktop networking and Windows Firewall before changing bind addresses or firewall rules. Any firewall exception must be scoped to the Docker Desktop host-side network, not the whole LAN or Internet.

## 7. Useful operations

    # Container status and logs
    docker compose --env-file .env -f compose.dev.yaml ps
    docker compose --env-file .env -f compose.dev.yaml logs --tail 200 web
    docker compose --env-file .env -f compose.dev.yaml logs --tail 200 ai

    # Rebuild after changing Dockerized code
    docker compose --env-file .env -f compose.dev.yaml up --build -d

    # Stop containers (keeps images and configuration)
    docker compose --env-file .env -f compose.dev.yaml down

    # Remove containers and the Compose network; no database is deleted
    docker compose --env-file .env -f compose.dev.yaml down --remove-orphans

Do not use docker compose down -v for this setup; no database volume is managed by Compose and volume deletion is unnecessary.

## 8. Current limitations and release gates

- This is a local development/pilot topology, not a public deployment profile.
- OIDC is intentionally not configured in the local Development bootstrap. The shared API key is only a local test identity; do not use it for external users.
- SQL migrations and database verification remain separate, explicit steps. Starting containers does not apply migrations.
- The current branch targets .NET 9. Upgrade and test on .NET 10 LTS before public production launch.
- Docker build, app health, API-to-SQL, Web-to-API, API-to-AI, automated tests and tenant-isolation tests must all pass before calling the pilot accepted.
