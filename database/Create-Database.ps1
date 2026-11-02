[CmdletBinding()]
param(
    [string]$SqlInstance = 'localhost',
    [ValidateSet('CreatorFlow_QC')]
    [string]$Database = 'CreatorFlow_QC',
    [bool]$TrustServerCertificate = $true
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($SqlInstance)) { throw 'SqlInstance is required.' }
$hostName = ($SqlInstance -split '\\')[0].Trim()
if (@('.', '(local)', 'localhost', $env:COMPUTERNAME) -notcontains $hostName) { throw "Refusing non-local SQL instance '$SqlInstance'." }
if (-not (Get-Module -ListAvailable -Name SqlServer)) { throw 'SqlServer PowerShell module is missing. Run Install-Module SqlServer -Scope AllUsers.' }
Import-Module SqlServer -ErrorAction Stop
$exists = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -Query "SELECT name FROM sys.databases WHERE name = N'CreatorFlow_QC';" -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop)
if ($exists.Count -eq 0) {
    Write-Host 'Creating isolated database CreatorFlow_QC...'
    Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -Query 'CREATE DATABASE [CreatorFlow_QC];' -TrustServerCertificate:$TrustServerCertificate -AbortOnError -ErrorAction Stop
    Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -AbortOnError -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
EXEC sys.sp_addextendedproperty @name=N'CreatorFlowEnvironment', @value=N'QC';
EXEC sys.sp_addextendedproperty @name=N'CreatorFlowRepository', @value=N'CreatorFlow-AI';
"@
    Write-Host 'Database created and tagged.'
}
else {
    $tags = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT
    MAX(CASE WHEN name=N'CreatorFlowEnvironment' THEN CONVERT(nvarchar(128), value) END) AS EnvironmentTag,
    MAX(CASE WHEN name=N'CreatorFlowRepository' THEN CONVERT(nvarchar(128), value) END) AS RepositoryTag
FROM sys.extended_properties
WHERE class=0;
"@)
    if ($tags.Count -ne 1 -or $tags[0].EnvironmentTag -cne 'QC' -or $tags[0].RepositoryTag -cne 'CreatorFlow-AI') { throw 'CreatorFlow_QC already exists but does not carry the exact QC/repository tags. Refusing to adopt or retag it.' }
    Write-Host 'Existing CreatorFlow_QC identity tags verified; no database changes made.'
}
$state = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT state_desc AS StateDesc, is_read_only AS IsReadOnly FROM sys.databases WHERE name=N'CreatorFlow_QC';")
if ($state.Count -ne 1 -or $state[0].StateDesc -ne 'ONLINE' -or $state[0].IsReadOnly -ne 0) { throw 'CreatorFlow_QC must be ONLINE and read-write.' }
Write-Host 'Database provisioning check passed: CreatorFlow_QC.'
