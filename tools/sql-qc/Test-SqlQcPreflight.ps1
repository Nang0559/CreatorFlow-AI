[CmdletBinding()]
param([string]$SqlInstance=$env:CREATORFLOW_QC_SQL_INSTANCE,[string]$Database=$env:CREATORFLOW_QC_DATABASE)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$failures=[System.Collections.Generic.List[string]]::new()
function Need-Command([string]$Name) { if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { $failures.Add("Missing command: $Name") } }
Write-Host 'CreatorFlow-AI SQL QC preflight'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { $failures.Add('Windows is required.') }
if (-not [Environment]::Is64BitOperatingSystem) { $failures.Add('64-bit Windows is required.') }
if ($PSVersionTable.PSVersion.Major -lt 5) { $failures.Add('PowerShell 5.1 or later is required.') }
Need-Command git; Need-Command dotnet; Need-Command sqlcmd
if (-not (Get-Module -ListAvailable -Name SqlServer)) { $failures.Add('SqlServer PowerShell module is missing.') }
if ([string]::IsNullOrWhiteSpace($SqlInstance)) { $failures.Add('CREATORFLOW_QC_SQL_INSTANCE is not configured.') }
else { $h=($SqlInstance -split '\\')[0].Trim(); if (@('.','(local)','localhost',$env:COMPUTERNAME) -notcontains $h) { $failures.Add("SQL instance must be local: $SqlInstance") } }
if ([string]::IsNullOrWhiteSpace($Database)) { $Database='CreatorFlow_QC' }
if ($Database -cne 'CreatorFlow_QC') { $failures.Add("Only CreatorFlow_QC is allowed, received '$Database'.") }
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
 $sdks=& dotnet --list-sdks 2>$null
 if (-not $sdks) { $failures.Add('No .NET SDK detected.') } else { Write-Host "Installed .NET SDKs: $($sdks -join ', ')" }
}
if ($failures.Count -gt 0) { Write-Error ("Preflight FAILED: "+($failures -join ' | ')); exit 1 }
Write-Host 'Preflight PASSED. SQL identity and QC tags are checked by Protect-QcDatabase.ps1.'
