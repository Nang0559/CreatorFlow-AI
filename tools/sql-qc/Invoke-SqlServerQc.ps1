[CmdletBinding()]
param([string]$SqlInstance=$env:CREATORFLOW_QC_SQL_INSTANCE,[string]$Database=$env:CREATORFLOW_QC_DATABASE,[switch]$SkipDotNetTests)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $PSScriptRoot 'Test-SqlQcPreflight.ps1') -SqlInstance $SqlInstance -Database $Database
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Preflight failed.' }
& (Join-Path $PSScriptRoot 'Protect-QcDatabase.ps1') -SqlInstance $SqlInstance -Database $Database
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Database safety guard failed.' }
Import-Module SqlServer -ErrorAction Stop
$result=Invoke-Sqlcmd -ServerInstance $SqlInstance -Database 'CreatorFlow_QC' -Query "SELECT DB_NAME() AS DatabaseName, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion, 1 AS ConnectivityCheck;" -ErrorAction Stop
if ($result.DatabaseName -cne 'CreatorFlow_QC' -or $result.ConnectivityCheck -ne 1) { throw 'SQL connectivity smoke test failed.' }
Write-Host "SQL connectivity passed: $($result.DatabaseName), SQL $($result.ProductVersion)."
if (-not $SkipDotNetTests) {
 Push-Location $root
 try { dotnet test CreatorFlow.sln --configuration Release; if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $LASTEXITCODE" } }
 finally { Pop-Location }
}
Write-Host 'CreatorFlow SQL QC completed.'
