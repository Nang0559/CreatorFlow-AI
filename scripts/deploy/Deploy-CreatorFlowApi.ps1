<#
.SYNOPSIS
  Publishes the API into a new release folder, switches <DeployRoot>\current to it, restarts the
  Scheduled Task and verifies /health. If the new release is not healthy it switches back to the
  previous release and fails the run, so a bad build does not leave the service down.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = 'src\CreatorFlow.Api\CreatorFlow.Api.csproj',
    [string]$DeployRoot = 'E:\CreatorFlowDeploy',
    [string]$EnvFile = 'E:\Projects\CreatorFlow-AI\.env',
    [string]$TaskName = 'CreatorFlow-Api',
    [int]$Port = 7001,
    [string]$Revision = '',
    [int]$KeepReleases = 5,
    [int]$HealthTimeoutSeconds = 90,
    # First migration from a manually started API: also stop a CreatorFlow.Api process that is not
    # under DeployRoot. Without this switch the script refuses to touch foreign listeners.
    [switch]$AdoptLegacyProcess
)

$ErrorActionPreference = 'Stop'
$releasesDir = Join-Path $DeployRoot 'releases'
$currentLink = Join-Path $DeployRoot 'current'
$binDir = Join-Path $DeployRoot 'bin'

function Get-PortOwners {
    $connections = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
    return @($connections | Select-Object -ExpandProperty OwningProcess -Unique)
}

function Stop-Api {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        $owners = Get-PortOwners
        if ($owners.Count -eq 0) { return }
        foreach ($processId in $owners) {
            $info = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
            $commandLine = if ($info) { [string]$info.CommandLine } else { '' }
            $ours = $commandLine -like "*$DeployRoot*"
            $legacy = $commandLine -match 'CreatorFlow\.Api'
            if ($ours -or ($AdoptLegacyProcess -and $legacy)) {
                Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
            } else {
                throw "Port $Port is held by process $processId ($commandLine), which is not this service. Refusing to stop it (use -AdoptLegacyProcess only for an old CreatorFlow.Api process)."
            }
        }
        Start-Sleep -Seconds 1
    }
    throw "Port $Port is still in use after stopping the service."
}

function Get-CurrentRelease {
    if (-not (Test-Path -LiteralPath $currentLink)) { return $null }
    $target = (Get-Item -LiteralPath $currentLink -Force).Target
    if ($target -is [array]) { $target = $target[0] }
    return $target
}

function Set-CurrentRelease([string]$target) {
    if (Test-Path -LiteralPath $currentLink) { [System.IO.Directory]::Delete($currentLink) }
    New-Item -ItemType Junction -Path $currentLink -Target $target | Out-Null
}

function Wait-Healthy {
    $deadline = (Get-Date).AddSeconds($HealthTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return $true }
        } catch { }
    }
    return $false
}

function Show-LogTail {
    $log = Join-Path (Join-Path $DeployRoot 'logs') ("api-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 60 } else { Write-Host 'No API log found.' }
}

# ---- Preflight -------------------------------------------------------------------------------
if (-not (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)) {
    throw "Scheduled task '$TaskName' is not registered. Run scripts\deploy\Register-CreatorFlowApiTask.ps1 first."
}
if (-not (Test-Path -LiteralPath $EnvFile)) {
    throw "Required .env file was not found at $EnvFile; refusing to deploy."
}
if (-not (Select-String -LiteralPath $EnvFile -Pattern '^\s*(export\s+)?CREATORFLOW_DATA_PROTECTION_KEYS_PATH\s*=' -Quiet)) {
    Write-Warning 'CREATORFLOW_DATA_PROTECTION_KEYS_PATH is not set in the .env file. Keys fall back to the service account profile; tenant credentials become unreadable if the account changes.'
}

# ---- Publish ---------------------------------------------------------------------------------
$suffix = if ($Revision) { $Revision.Substring(0, [Math]::Min(8, $Revision.Length)) } else { 'manual' }
$releaseName = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $suffix
$releaseDir = Join-Path $releasesDir $releaseName
New-Item -ItemType Directory -Force -Path $releasesDir, $binDir | Out-Null

& dotnet publish $ProjectPath -c Release -o $releaseDir --no-self-contained
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed; the running service was not touched.' }
if (-not (Test-Path -LiteralPath (Join-Path $releaseDir 'CreatorFlow.Api.dll'))) {
    throw 'Published output does not contain CreatorFlow.Api.dll; the running service was not touched.'
}
Set-Content -LiteralPath (Join-Path $releaseDir 'release.txt') -Value $Revision -Encoding utf8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-CreatorFlowApi.ps1') -Destination $binDir -Force

# ---- Switch with rollback --------------------------------------------------------------------
$previous = Get-CurrentRelease
Write-Host "Previous release: $previous"
Write-Host "New release:      $releaseDir"

Stop-Api
Set-CurrentRelease $releaseDir
Start-ScheduledTask -TaskName $TaskName

if (Wait-Healthy) {
    Write-Host "API is healthy on port $Port (release $releaseName)."
} else {
    Show-LogTail
    if ($previous -and (Test-Path -LiteralPath $previous)) {
        Write-Warning "New release is not healthy; rolling back to $previous."
        Stop-Api
        Set-CurrentRelease $previous
        Start-ScheduledTask -TaskName $TaskName
        $restored = Wait-Healthy
        throw ("Deployment of $releaseName failed health check. Rollback " + $(if ($restored) { 'succeeded.' } else { 'ALSO FAILED; the service is down.' }))
    }
    throw "Deployment of $releaseName failed health check and there is no previous release to roll back to."
}

# ---- Retention -------------------------------------------------------------------------------
$keep = @($releaseDir, $previous) | Where-Object { $_ }
Get-ChildItem -LiteralPath $releasesDir -Directory |
    Sort-Object Name -Descending |
    Select-Object -Skip $KeepReleases |
    Where-Object { $keep -notcontains $_.FullName } |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }