[CmdletBinding()]
param(
    [string]$RepositoryPath = 'E:\Projects\CreatorFlow-AI',
    [string]$Branch = 'feature/trend-selection-safety-intelligence',
    [string]$SqlInstance = 'localhost',
    [string]$Database = 'CreatorFlow_QC',
    [switch]$SkipGitPull,
    [switch]$SkipDotNetTests,
    [switch]$SkipDocker,
    [switch]$SkipApiStart,
    [switch]$EnableLanAccess,
    [switch]$ConfirmLegacyWorkspaceAssignment,
    [int]$HealthTimeoutSeconds = 90
)

# CreatorFlow-AI HP Z240 one-run setup.
# Safe to rerun: refuses dirty/wrong Git state, SQL migrations are checksum-ledgered,
# Docker Compose is declarative, and existing healthy API processes are reused.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Stage([string]$Message) {
    Write-Host ''
    Write-Host ('=' * 76) -ForegroundColor DarkCyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host ('=' * 76) -ForegroundColor DarkCyan
}
function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing command '$Name'. Install it and rerun this script."
    }
}
function Wait-Http([string]$Url, [int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-RestMethod -Uri $Url -TimeoutSec 4 -ErrorAction Stop
            return $response
        } catch {
            Start-Sleep -Seconds 3
        }
    } while ((Get-Date) -lt $deadline)
    throw "Health endpoint did not become ready: $Url"
}

try {
    Write-Stage '0/8 - Validate HP Z240 environment'
    if (-not (Test-Path -LiteralPath $RepositoryPath -PathType Container)) {
        throw "Repository path not found: $RepositoryPath"
    }
    Set-Location -LiteralPath $RepositoryPath
    Require-Command git
    Require-Command dotnet
    Require-Command docker

    if ($EnableLanAccess) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'The -EnableLanAccess option requires an elevated PowerShell window so it can add a narrowly scoped Windows Firewall rule.'
        }
        $privateProfile = @(Get-NetConnectionProfile -ErrorAction SilentlyContinue |
            Where-Object { $_.NetworkCategory -eq 'Private' -and ($_.IPv4Connectivity -ne 'Disconnected' -or $_.IPv6Connectivity -ne 'Disconnected') })
        if ($privateProfile.Count -eq 0) {
            throw 'The active network must be marked Private before enabling phone access. Do not enable LAN access on a Public or untrusted network.'
        }
    }

    $branchNow = (git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read current Git branch.' }
    if ($branchNow -cne $Branch) { throw "Wrong branch '$branchNow'. Expected '$Branch'. Switch branch and rerun." }

    $dirty = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read Git working tree status.' }
    if ($dirty.Count -gt 0) {
        $dirty | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
        throw 'Tracked/untracked local changes detected. Commit or safely back them up before rerunning. .env is ignored and is not included.'
    }

    if (-not $SkipGitPull) {
        git fetch origin $Branch
        if ($LASTEXITCODE -ne 0) { throw 'git fetch failed.' }
        git pull --ff-only origin $Branch
        if ($LASTEXITCODE -ne 0) { throw 'Fast-forward pull failed. No reset/force operation was attempted.' }
    }
    $head = (git rev-parse --short HEAD).Trim()
    Write-Host "Branch: $Branch"
    Write-Host "Commit: $head"

    if (-not (Test-Path -LiteralPath '.env' -PathType Leaf)) {
        throw 'Missing .env. Copy .env.example to .env and configure secrets locally. Never paste secrets into chat.'
    }
    if (-not (Test-Path -LiteralPath 'compose.dev.yaml' -PathType Leaf)) { throw 'compose.dev.yaml is missing.' }
    if (-not (Test-Path -LiteralPath 'database\Apply-Migrations.ps1' -PathType Leaf)) { throw 'SQL migration runner is missing.' }

    Write-Stage '1/8 - Validate local secrets without displaying them'
    . '.\scripts\docker\Import-LocalPilotEnv.ps1'
    Import-LocalPilotEnv -Path (Join-Path $RepositoryPath '.env')
    if ($env:ConnectionStrings__CreatorFlow -notmatch 'Database=CreatorFlow_QC') {
        throw 'ConnectionStrings__CreatorFlow must target CreatorFlow_QC for this QC setup.'
    }
    if ($env:ConnectionStrings__CreatorFlow -match 'Server=(?!localhost|\.|\(local\))') {
        throw 'Refusing a non-local SQL connection string in the HP Z240 QC setup.'
    }

    Write-Stage '2/8 - Verify SQL Server and SqlServer PowerShell module'
    if (-not (Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue)) {
        throw 'Default SQL Server service MSSQLSERVER was not found.'
    }
    $sqlService = Get-Service -Name 'MSSQLSERVER'
    if ($sqlService.Status -ne 'Running') { throw 'SQL Server service MSSQLSERVER is not running. Start it, then rerun.' }
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        throw 'PowerShell module SqlServer is missing. Run: Install-Module SqlServer -Scope CurrentUser; then rerun.'
    }
    Import-Module SqlServer -ErrorAction Stop
    $dbState = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -TrustServerCertificate -ErrorAction Stop -Query @"
SELECT name, state_desc, is_read_only
FROM sys.databases
WHERE name=N'$Database';
"@)
    if ($dbState.Count -ne 1 -or $dbState[0].state_desc -ne 'ONLINE' -or $dbState[0].is_read_only -ne 0) {
        throw "Database $Database must already exist, be ONLINE and writable. This script will not create, drop, restore or retag databases."
    }

    $tenantReadiness = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate -ErrorAction Stop -Query @"
SELECT
    CASE WHEN OBJECT_ID(N'dbo.Tenants',N'U') IS NOT NULL
       AND (SELECT COUNT(*) FROM dbo.SchemaMigrations WHERE MigrationId IN (N'011_tenant_core',N'012_tenant_ownership',N'013_tenant_lifecycle')) = 3
    THEN 1 ELSE 0 END AS TenantSchemaReady;
"@)
    $tenantSchemaReady = $tenantReadiness.Count -eq 1 -and $tenantReadiness[0].TenantSchemaReady -eq 1
    if (-not $tenantSchemaReady -and -not $ConfirmLegacyWorkspaceAssignment) {
        throw 'Tenant schema 011–013 is not fully installed. Review the tenant migration ownership rules first; only then rerun with -ConfirmLegacyWorkspaceAssignment. No tenant migration has been applied by this script.'
    }

    Write-Stage '3/8 - Apply checksum-protected SQL migrations (rerunnable)'
    if ($ConfirmLegacyWorkspaceAssignment) {
        & '.\database\Apply-Migrations.ps1' -SqlInstance $SqlInstance -Database $Database -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment
    } else {
        & '.\database\Apply-Migrations.ps1' -SqlInstance $SqlInstance -Database $Database
    }
    if ($LASTEXITCODE -ne 0) { throw 'SQL migration runner failed.' }

    Write-Stage '4/8 - Verify SQL schema and migration checksums'
    & '.\database\Verify-Database.ps1' -SqlInstance $SqlInstance -Database $Database -RequireTenantMigrations
    if ($LASTEXITCODE -ne 0) { throw 'SQL schema verification failed.' }

    Write-Stage '5/8 - Build and test .NET solution'
    dotnet --version
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK is unavailable.' }
    dotnet restore '.\CreatorFlow.sln'
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet build '.\CreatorFlow.sln' --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    if (-not $SkipDotNetTests) {
        dotnet test '.\CreatorFlow.sln' --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    } else {
        Write-Warning 'Dotnet tests explicitly skipped.'
    }

    Write-Stage '5b/8 - Detect optional video editors and hardware'
    $capCutCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'CapCut\Apps\CapCut.exe'),
        (Join-Path $env:ProgramFiles 'CapCut\CapCut.exe'),
        'C:\Program Files\CapCut\CapCut.exe'
    )
    $capCutPath = $capCutCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($capCutPath) { Write-Host "CapCut Desktop: FOUND ($capCutPath)" } else { Write-Host 'CapCut Desktop: not detected (optional; install from the official CapCut website).' }

    $aeRender = $null
    $adobeRoot = Join-Path $env:ProgramFiles 'Adobe'
    if (Test-Path -LiteralPath $adobeRoot) {
        $aeRender = Get-ChildItem -LiteralPath $adobeRoot -Directory -Filter 'Adobe After Effects*' -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName 'Support Files\aerender.exe' } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if ($aeRender) { Write-Host "After Effects renderer: FOUND ($aeRender)" } else { Write-Host 'After Effects: not detected (optional; requires an Adobe license).' }

    $cpuInfo = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Name
    $gpuInfo = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    $ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue).TotalPhysicalMemory / 1GB, 1)
    Write-Host "CPU: $cpuInfo"
    Write-Host "RAM: $ramGb GB"
    Write-Host "GPU: $($gpuInfo -join '; ')"
    if (-not $SkipDocker) {
        Write-Stage '6/8 - Build/start AI and Web containers'
        docker info | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Docker engine is not available. Start Docker Desktop and rerun.' }
        if ($EnableLanAccess) {
            $activeProfile = Get-NetConnectionProfile -ErrorAction SilentlyContinue |
                Where-Object { $_.IPv4Connectivity -ne 'Disconnected' -or $_.IPv6Connectivity -ne 'Disconnected' }
            if (-not $activeProfile -or @($activeProfile | Where-Object { $_.NetworkCategory -eq 'Private' }).Count -eq 0) {
                throw 'LAN access requires the active Windows network profile to be Private. Change the trusted home/work network to Private, then rerun with -EnableLanAccess.'
            }
            $env:CREATORFLOW_WEB_BIND_IP = '0.0.0.0'
        } else {
            $env:CREATORFLOW_WEB_BIND_IP = '127.0.0.1'
        }
        docker compose --env-file .env -f compose.dev.yaml config --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose configuration validation failed.' }
        docker compose --env-file .env -f compose.dev.yaml up --build -d
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose build/start failed.' }

        Write-Stage '7/8 - Wait for AI health and readiness'
        Wait-Http -Url 'http://127.0.0.1:8001/health' -TimeoutSeconds $HealthTimeoutSeconds | Out-Null
        Wait-Http -Url 'http://127.0.0.1:8001/ready' -TimeoutSeconds $HealthTimeoutSeconds | Out-Null
        docker compose --env-file .env -f compose.dev.yaml ps
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read Docker Compose status.' }
        if ($EnableLanAccess) {
            $rule = Get-NetFirewallRule -Name 'CreatorFlow-Web-LAN-8080' -ErrorAction SilentlyContinue
            if (-not $rule) {
                New-NetFirewallRule -Name 'CreatorFlow-Web-LAN-8080' -DisplayName 'CreatorFlow Web – Private LAN only' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8080 -RemoteAddress LocalSubnet -Profile Private | Out-Null
            }
            Write-Host 'Private LAN access enabled for Web only (TCP 8080, LocalSubnet, Private profile). API, AI and SQL ports remain unpublished.'
            Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' -and $_.AddressState -eq 'Preferred' } |
                ForEach-Object { Write-Host "Phone URL: http://$($_.IPAddress):8080" }
        } else {
            Write-Host 'Web is bound to localhost only. Add -EnableLanAccess to allow phone access on a trusted Private LAN.'
        }
    } else {
        Write-Warning 'Docker build/start and AI health checks explicitly skipped.'
    }

    if (-not $SkipApiStart) {
        Write-Stage '8/8 - Start/reuse native Windows API'
        $apiHealth = $null
        try { $apiHealth = Invoke-RestMethod -Uri 'http://127.0.0.1:7001/health' -TimeoutSec 3 -ErrorAction Stop } catch {}
        if ($null -eq $apiHealth) {
            $listener = Get-NetTCPConnection -LocalPort 7001 -State Listen -ErrorAction SilentlyContinue
            if ($listener) {
                throw 'Port 7001 is already occupied by a process that does not answer the CreatorFlow API health endpoint. Stop/identify it manually; script will not kill processes.'
            }
            $env:ASPNETCORE_ENVIRONMENT = 'Development'
            $env:ASPNETCORE_URLS = 'http://0.0.0.0:7001'
            $env:Tenant__AllowApiKeyBootstrap = 'true'
            $env:Tenant__BootstrapTenantId = '00000000-0000-4000-8000-000000000001'
            $env:CREATORFLOW_AI_BASE_URL = 'http://127.0.0.1:8001'
            $logDir = Join-Path $RepositoryPath 'artifacts\local-pilot'
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
            $stdout = Join-Path $logDir 'api.stdout.log'
            $stderr = Join-Path $logDir 'api.stderr.log'
            $apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @(
                'run', '--no-build', '--project', 'src\CreatorFlow.Api\CreatorFlow.Api.csproj', '--no-launch-profile'
            ) -WorkingDirectory $RepositoryPath -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
            try {
                Wait-Http -Url 'http://127.0.0.1:7001/health' -TimeoutSeconds $HealthTimeoutSeconds | Out-Null
            } catch {
                if ($apiProcess -and -not $apiProcess.HasExited) {
                    Write-Warning "API process $($apiProcess.Id) is still running. Logs: $stdout ; $stderr"
                }
                throw
            }
        } else {
            Write-Host 'A healthy CreatorFlow API is already listening on port 7001; reused without restarting.'
        }
    } else {
        Write-Warning 'Native API start explicitly skipped.'
    }

    Write-Stage 'FINAL - Local pilot verification'
    if (-not $SkipDocker) {
        Wait-Http -Url 'http://127.0.0.1:8001/health' -TimeoutSeconds 10 | Out-Null
        Write-Host 'AI health: PASS'
        Write-Host 'AI ready:  PASS'
    }
    if (-not $SkipApiStart) {
        Wait-Http -Url 'http://127.0.0.1:7001/health' -TimeoutSeconds 10 | Out-Null
        Write-Host 'API health: PASS'
    }
    try {
        $web = Invoke-WebRequest -Uri 'http://127.0.0.1:8080' -TimeoutSec 10 -UseBasicParsing
        Write-Host "Web HTTP: $([int]$web.StatusCode)"
    } catch {
        Write-Warning 'Web did not return HTTP yet. Check: docker compose --env-file .env -f compose.dev.yaml logs --tail 100 web'
    }
    Write-Host ''
    Write-Host 'COMPLETED. Open http://localhost:8080' -ForegroundColor Green
    Write-Host 'This script does not log in to Douyin/TikTok, create platform sessions, or perform account actions.'
    Write-Host "Run again safely with: .\scripts\Setup-HPZ240.ps1"
}
catch {
    Write-Host ''
    Write-Host "STOPPED SAFELY: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'No database drop/restore, Git reset/force, secret display, or process termination was performed.'
    exit 1
}
