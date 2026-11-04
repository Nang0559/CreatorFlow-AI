[CmdletBinding()]
param([switch]$Resync)

$ErrorActionPreference = 'Stop'

Write-Host '=== Windows UTC clock ==='
[pscustomobject]@{
    LocalTime = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff zzz')
    UtcTime   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss.fffZ')
} | Format-List

Write-Host '=== Windows Time Service ==='
$service = Get-Service -Name W32Time
if ($service.Status -ne 'Running') {
    Write-Warning "Windows Time service is $($service.Status). Starting it..."
    Start-Service -Name W32Time
}
w32tm.exe /query /status

if ($Resync) {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Open PowerShell as Administrator and rerun with -Resync.'
    }
    Write-Host '=== Requesting NTP resynchronization ==='
    w32tm.exe /resync /force
    if ($LASTEXITCODE -ne 0) { throw "w32tm /resync failed with exit code $LASTEXITCODE." }
    Start-Sleep -Seconds 2
    w32tm.exe /query /status
}

Write-Host ''
Write-Host 'Note: Docker Desktop and WSL2 use the Windows host clock. Restart Docker Desktop/WSL only if their displayed UTC time remains different after Windows has synchronized.'
