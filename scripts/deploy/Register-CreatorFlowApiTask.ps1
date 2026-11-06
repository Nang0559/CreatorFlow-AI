<#
.SYNOPSIS
  One-time setup (run from an elevated PowerShell on the Z240): registers the Scheduled Task that
  runs the API. Use the SAME Windows account that previously ran the API so that the DPAPI-protected
  Data Protection keys (tenant connector credentials) can still be decrypted.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UserName,
    [string]$TaskName = 'CreatorFlow-Api',
    [string]$DeployRoot = 'E:\CreatorFlowDeploy',
    [string]$EnvFile = 'E:\Projects\CreatorFlow-AI\.env',
    [int]$Port = 7001
)

$ErrorActionPreference = 'Stop'
$credential = Get-Credential -UserName $UserName -Message "Password for the account that runs CreatorFlow.Api"
$plainPassword = $credential.GetNetworkCredential().Password

$binDir = Join-Path $DeployRoot 'bin'
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run-CreatorFlowApi.ps1') -Destination $binDir -Force
$launcher = Join-Path $binDir 'Run-CreatorFlowApi.ps1'

$argument = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -DeployRoot "{1}" -EnvFile "{2}" -Port {3}' -f $launcher, $DeployRoot, $EnvFile, $Port
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argument
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Seconds 0)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -User $credential.UserName -Password $plainPassword -RunLevel Limited -Force | Out-Null
$plainPassword = $null

Write-Host "Registered task '$TaskName' (runs as $($credential.UserName))."
Write-Host "The account that runs the GitHub runner must be allowed to start/stop this task (administrator is sufficient)."
Write-Host "Set CREATORFLOW_DATA_PROTECTION_KEYS_PATH in $EnvFile to a fixed folder readable only by $($credential.UserName)."