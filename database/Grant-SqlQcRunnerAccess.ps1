[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param(
    [string]$SqlInstance = 'localhost',
    [ValidateSet('CreatorFlow_QC')]
    [string]$Database = 'CreatorFlow_QC',
    [string]$RunnerLogin = 'NT AUTHORITY\NETWORK SERVICE',
    [bool]$TrustServerCertificate = $true
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostName = ($SqlInstance -split '\\')[0].Trim()
if (@('.', '(local)', 'localhost', $env:COMPUTERNAME) -notcontains $hostName) {
    throw "Refusing non-local SQL instance '$SqlInstance'."
}
if ($Database -cne 'CreatorFlow_QC') {
    throw 'This script is restricted to CreatorFlow_QC.'
}
if ([string]::IsNullOrWhiteSpace($RunnerLogin) -or $RunnerLogin.Length -gt 256) {
    throw 'RunnerLogin is required and must be at most 256 characters.'
}

Import-Module SqlServer -ErrorAction Stop
$serverIdentity = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT IS_SRVROLEMEMBER(N'sysadmin') AS IsSysAdmin, SUSER_SNAME() AS SqlLogin;")
if ($serverIdentity.Count -ne 1 -or $serverIdentity[0].IsSysAdmin -ne 1) {
    throw 'Run this one-time permission setup from an elevated SQL sysadmin identity.'
}

$databaseIdentity = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT
    MAX(CASE WHEN name=N'CreatorFlowEnvironment' THEN CONVERT(nvarchar(128), value) END) AS EnvironmentTag,
    MAX(CASE WHEN name=N'CreatorFlowRepository' THEN CONVERT(nvarchar(128), value) END) AS RepositoryTag
FROM sys.extended_properties
WHERE class=0;
"@)
if ($databaseIdentity.Count -ne 1 -or $databaseIdentity[0].EnvironmentTag -cne 'QC' -or
    $databaseIdentity[0].RepositoryTag -cne 'CreatorFlow-AI') {
    throw 'Database identity tags do not match CreatorFlow-AI QC. No permissions were changed.'
}

$loginLiteral = $RunnerLogin.Replace("'", "''")
$loginIdentifier = $RunnerLogin.Replace(']', ']]')
$serverSql = @"
IF SUSER_ID(N'$loginLiteral') IS NULL
    CREATE LOGIN [$loginIdentifier] FROM WINDOWS;
"@
$databaseSql = @"
IF DATABASE_PRINCIPAL_ID(N'$loginLiteral') IS NULL
    CREATE USER [$loginIdentifier] FOR LOGIN [$loginIdentifier];

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    INNER JOIN sys.database_principals rolePrincipal ON rolePrincipal.principal_id=drm.role_principal_id
    INNER JOIN sys.database_principals memberPrincipal ON memberPrincipal.principal_id=drm.member_principal_id
    WHERE rolePrincipal.name=N'db_ddladmin' AND memberPrincipal.name=N'$loginLiteral'
)
    ALTER ROLE db_ddladmin ADD MEMBER [$loginIdentifier];

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    INNER JOIN sys.database_principals rolePrincipal ON rolePrincipal.principal_id=drm.role_principal_id
    INNER JOIN sys.database_principals memberPrincipal ON memberPrincipal.principal_id=drm.member_principal_id
    WHERE rolePrincipal.name=N'db_datareader' AND memberPrincipal.name=N'$loginLiteral'
)
    ALTER ROLE db_datareader ADD MEMBER [$loginIdentifier];

IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members drm
    INNER JOIN sys.database_principals rolePrincipal ON rolePrincipal.principal_id=drm.role_principal_id
    INNER JOIN sys.database_principals memberPrincipal ON memberPrincipal.principal_id=drm.member_principal_id
    WHERE rolePrincipal.name=N'db_datawriter' AND memberPrincipal.name=N'$loginLiteral'
)
    ALTER ROLE db_datawriter ADD MEMBER [$loginIdentifier];

GRANT ALTER ON SCHEMA::dbo TO [$loginIdentifier];
"@

$target = "$RunnerLogin on $SqlInstance/$Database"
if (-not $PSCmdlet.ShouldProcess($target, 'Grant db_ddladmin, db_datareader, db_datawriter and ALTER on dbo schema')) {
    return
}

Invoke-Sqlcmd -ServerInstance $SqlInstance -Database master -TrustServerCertificate:$TrustServerCertificate -AbortOnError -ErrorAction Stop -Query $serverSql
Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -AbortOnError -ErrorAction Stop -Query $databaseSql

$permissions = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
EXECUTE AS LOGIN = N'$loginLiteral';
SELECT
    USER_NAME() AS DatabaseUser,
    IS_MEMBER(N'db_owner') AS IsDbOwner,
    IS_MEMBER(N'db_ddladmin') AS IsDbDdlAdmin,
    IS_MEMBER(N'db_datareader') AS IsDbDataReader,
    IS_MEMBER(N'db_datawriter') AS IsDbDataWriter,
    HAS_PERMS_BY_NAME(N'dbo',N'SCHEMA',N'ALTER') AS CanAlterDboSchema;
REVERT;
"@)
if ($permissions.Count -ne 1 -or $permissions[0].IsDbDdlAdmin -ne 1 -or
    $permissions[0].IsDbDataReader -ne 1 -or $permissions[0].IsDbDataWriter -ne 1 -or
    $permissions[0].CanAlterDboSchema -ne 1) {
    throw 'Runner permissions are still insufficient. Review the SQL role and schema grants.'
}
$permissions | Format-List
Write-Host "Least-privilege SQL QC permissions are ready for $RunnerLogin on CreatorFlow_QC."
