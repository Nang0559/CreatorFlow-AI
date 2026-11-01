[CmdletBinding()]
param([string]$SqlInstance=$env:CREATORFLOW_QC_SQL_INSTANCE,[string]$Database=$env:CREATORFLOW_QC_DATABASE)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($SqlInstance)) { throw 'CREATORFLOW_QC_SQL_INSTANCE is required.' }
if ([string]::IsNullOrWhiteSpace($Database)) { $Database='CreatorFlow_QC' }
if ($Database -cne 'CreatorFlow_QC') { throw "Refusing database '$Database'. Only CreatorFlow_QC is allowed." }
$hostName=($SqlInstance -split '\\')[0].Trim()
if (@('.','(local)','localhost',$env:COMPUTERNAME) -notcontains $hostName) { throw "Refusing non-local SQL instance '$SqlInstance'." }
if (-not (Get-Module -ListAvailable -Name SqlServer)) { throw 'SqlServer module missing. Install-Module SqlServer -Scope CurrentUser.' }
Import-Module SqlServer -ErrorAction Stop
$query=@"
SET NOCOUNT ON;
SELECT d.state_desc AS StateDesc,d.is_read_only AS IsReadOnly,
 CAST(ep.value AS nvarchar(128)) AS EnvironmentTag,
 CAST(rp.value AS nvarchar(128)) AS RepositoryTag,
 IS_SRVROLEMEMBER(N'sysadmin') AS IsSysAdmin
FROM sys.databases d
LEFT JOIN sys.extended_properties ep ON ep.class=0 AND ep.name=N'CreatorFlowEnvironment'
LEFT JOIN sys.extended_properties rp ON rp.class=0 AND rp.name=N'CreatorFlowRepository'
WHERE d.name=N'CreatorFlow_QC';
"@
$rows=@(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -Query $query -ErrorAction Stop)
if ($rows.Count -ne 1) { throw 'CreatorFlow_QC database does not exist.' }
$r=$rows[0]
if ($r.StateDesc -ne 'ONLINE') { throw 'QC database is not ONLINE.' }
if ($r.IsReadOnly -ne 0) { throw 'QC database is READ_ONLY.' }
if ($r.EnvironmentTag -cne 'QC') { throw 'Missing CreatorFlowEnvironment=QC tag.' }
if ($r.RepositoryTag -cne 'CreatorFlow-AI') { throw 'Missing CreatorFlowRepository=CreatorFlow-AI tag.' }
if ($r.IsSysAdmin -ne 0) { throw 'Runner SQL principal is sysadmin; refusing execution.' }
Write-Host "QC database guard passed: $($r.StateDesc); CreatorFlow_QC; principal is not sysadmin."
