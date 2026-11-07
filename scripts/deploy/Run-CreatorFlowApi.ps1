<#
.SYNOPSIS
  Launcher executed by the "CreatorFlow-Api" Scheduled Task. Runs the published API from
  <DeployRoot>\current and appends its console output to a daily log file.
  The task (not the GitHub runner) owns the process, so it survives the end of a workflow job.
#>
[CmdletBinding()]
param(
    [string]$DeployRoot = 'E:\CreatorFlowDeploy',
    [string]$EnvFile = 'E:\Projects\CreatorFlow-AI\.env',
    [int]$Port = 7001
)

$ErrorActionPreference = 'Stop'
$app = Join-Path $DeployRoot 'current'
$dll = Join-Path $app 'CreatorFlow.Api.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "Published API not found: $dll" }

$logDir = Join-Path $DeployRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir ("api-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))

# The .env file is opt-in and only supplies values that are not already set in the process.
$env:CREATORFLOW_LOAD_DOTENV = 'true'
$env:CREATORFLOW_DOTENV_PATH = $EnvFile

# Bind on all interfaces by default so the Web container can reach host.docker.internal:<port>,
# unless ASPNETCORE_URLS is already provided by the process or by the .env file.
if (-not $env:ASPNETCORE_URLS) {
    $inFile = (Test-Path -LiteralPath $EnvFile) -and
        (Select-String -LiteralPath $EnvFile -Pattern '^\s*(export\s+)?ASPNETCORE_URLS\s*=' -Quiet)
    if (-not $inFile) { $env:ASPNETCORE_URLS = "http://0.0.0.0:$Port" }
}

Set-Location -LiteralPath $app
"[{0}] Starting CreatorFlow.Api from {1}" -f (Get-Date -Format 's'), $app | Out-File -FilePath $log -Append -Encoding utf8
& dotnet $dll 2>&1 | Out-File -FilePath $log -Append -Encoding utf8
"[{0}] CreatorFlow.Api exited with code {1}" -f (Get-Date -Format 's'), $LASTEXITCODE | Out-File -FilePath $log -Append -Encoding utf8
exit $LASTEXITCODE