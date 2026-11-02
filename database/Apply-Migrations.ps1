[CmdletBinding()]
param(
    [string]$SqlInstance = 'localhost',
    [ValidateSet('CreatorFlow_QC')]
    [string]$Database = 'CreatorFlow_QC',
    [switch]$IncludeTenantMigrations,
    [switch]$ConfirmLegacyWorkspaceAssignment,
    [bool]$TrustServerCertificate = $true
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($IncludeTenantMigrations -and -not $ConfirmLegacyWorkspaceAssignment) {
    throw 'Tenant migrations 011–013 create tenant identity, assign listed existing rows to legacy-workspace and extend tenant lifecycle. Pass -ConfirmLegacyWorkspaceAssignment only after reviewing that ownership assumption.'
}
$hostName = ($SqlInstance -split '\\')[0].Trim()
if (@('.', '(local)', 'localhost', $env:COMPUTERNAME) -notcontains $hostName) { throw "Refusing non-local SQL instance '$SqlInstance'." }
if (-not (Get-Module -ListAvailable -Name SqlServer)) { throw 'SqlServer PowerShell module is missing. Run Install-Module SqlServer -Scope AllUsers.' }
Import-Module SqlServer -ErrorAction Stop

$schemaPath = Join-Path $PSScriptRoot 'schema'
if (-not (Test-Path -LiteralPath $schemaPath -PathType Container)) { throw "Schema directory not found: $schemaPath" }
$identity = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT
    MAX(CASE WHEN name=N'CreatorFlowEnvironment' THEN CONVERT(nvarchar(128), value) END) AS EnvironmentTag,
    MAX(CASE WHEN name=N'CreatorFlowRepository' THEN CONVERT(nvarchar(128), value) END) AS RepositoryTag
FROM sys.extended_properties
WHERE class=0;
"@)
if ($identity.Count -ne 1 -or $identity[0].EnvironmentTag -cne 'QC' -or $identity[0].RepositoryTag -cne 'CreatorFlow-AI') {
    throw 'Migration target is not tagged exactly as CreatorFlow-AI QC. Run Create-Database.ps1 first; never retag an unknown database.'
}
$ledgerExists = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL THEN 0 ELSE 1 END AS ExistsFlag;")
if ($ledgerExists[0].ExistsFlag -eq 0) {
    $userTables = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT COUNT(*) AS TableCount FROM sys.tables WHERE is_ms_shipped=0;")
    if ($userTables[0].TableCount -gt 0) { throw 'Untracked user tables exist but dbo.SchemaMigrations is absent. Refusing to baseline/adopt an unknown schema.' }
    Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -AbortOnError -ErrorAction Stop -Query @"
CREATE TABLE dbo.SchemaMigrations
(
    MigrationId nvarchar(160) NOT NULL CONSTRAINT PK_SchemaMigrations PRIMARY KEY,
    ScriptName nvarchar(260) NOT NULL,
    ScriptSha256 char(64) NOT NULL,
    AppliedAt datetimeoffset(7) NOT NULL,
    AppliedBy nvarchar(256) NOT NULL
);
"@
}
$prefixes = @('001_', '002_', '003_', '004_', '005_', '006_', '007_', '008_', '009_', '010_', '014_', '015_')
$tenantState = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT COUNT(*) AS TenantMigrationCount,
       CASE WHEN OBJECT_ID(N'dbo.Tenants',N'U') IS NULL THEN 0 ELSE 1 END AS TenantTableExists
FROM dbo.SchemaMigrations
WHERE MigrationId IN (N'011_tenant_identity_foundation',N'012_tenant_data_isolation',N'013_tenant_membership_lifecycle');
"@)
$tenantMigrationCount = [int]$tenantState[0].TenantMigrationCount
$tenantTableExists = [int]$tenantState[0].TenantTableExists -eq 1
if ($tenantMigrationCount -notin @(0,3)) { throw 'Tenant migrations 011–013 are partially installed. Inspect the ledger and database before continuing.' }
if ($tenantMigrationCount -eq 0 -and $tenantTableExists) { throw 'dbo.Tenants exists without a complete tenant migration ledger. Refusing to guess schema ownership.' }
if ($tenantMigrationCount -eq 3 -and -not $tenantTableExists) { throw 'Tenant migrations are recorded but dbo.Tenants is missing.' }
if ($IncludeTenantMigrations -or $tenantMigrationCount -eq 3) {
    $prefixes += @('011_', '012_', '013_', '016_', '017_', '018_', '019_', '020_')
} else {
    Write-Warning 'Tenant migrations are not installed. Applying only non-tenant migrations 001–010 and 014–015; Share Inbox and video migrations 016–020 are deferred.'
}
$files = @(Get-ChildItem -LiteralPath $schemaPath -File -Filter '*.sql' | Where-Object {
    $name = $_.Name
    @($prefixes | Where-Object { $name.StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0
} | Sort-Object Name)
if ($files.Count -ne $prefixes.Count) { throw "Expected $($prefixes.Count) migration files, found $($files.Count). Check database/schema numbering." }

foreach ($file in $files) {
    $migrationId = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $safeId = $migrationId.Replace("'", "''")
    $safeName = $file.Name.Replace("'", "''")
    $applied = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT ScriptName, ScriptSha256 FROM dbo.SchemaMigrations WHERE MigrationId=N'$safeId';")
    if ($applied.Count -gt 0) {
        if ($applied[0].ScriptName -cne $file.Name -or $applied[0].ScriptSha256 -cne $hash) { throw "Migration checksum mismatch for $($file.Name). Applied migrations are immutable; add a new numbered migration instead." }
        Write-Host "SKIP $($file.Name) (already applied; checksum verified)"
        continue
    }
    Write-Host "APPLY $($file.Name)"
    Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -InputFile $file.FullName -AbortOnError -QueryTimeout 0 -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop
    Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -AbortOnError -ErrorAction Stop -Query @"
INSERT dbo.SchemaMigrations(MigrationId, ScriptName, ScriptSha256, AppliedAt, AppliedBy)
VALUES (N'$safeId', N'$safeName', '$hash', SYSUTCDATETIME(), ORIGINAL_LOGIN());
"@
    Write-Host "APPLIED $($file.Name)"
}
Write-Host "Migration execution completed for $Database."
