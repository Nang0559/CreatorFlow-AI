[CmdletBinding()]
param(
    [string]$SqlInstance=$env:CREATORFLOW_QC_SQL_INSTANCE,
    [string]$Database=$env:CREATORFLOW_QC_DATABASE,
    [switch]$SkipDotNetTests,
    [bool]$TrustServerCertificate=$true
)
$ErrorActionPreference='Stop'; Set-StrictMode -Version Latest
$root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $PSScriptRoot 'Test-SqlQcPreflight.ps1') -SqlInstance $SqlInstance -Database $Database
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Preflight failed.' }
& (Join-Path $PSScriptRoot 'Protect-QcDatabase.ps1') -SqlInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Database safety guard failed.' }

# Apply the complete migration set on a clean QC database, or advance a database
# whose tenant foundation (011-013) is already complete. Never infer ownership
# for a populated legacy database from an automated CI run.
Import-Module SqlServer -ErrorAction Stop
$migrationState = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @LedgerExists bit = CASE WHEN OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL THEN 0 ELSE 1 END;
DECLARE @TenantMigrationCount int = 0;
DECLARE @UserTableCount int = (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0);
IF @LedgerExists = 1
    SELECT @TenantMigrationCount = COUNT(*)
    FROM dbo.SchemaMigrations
    WHERE MigrationId IN (N'011_tenant_identity_foundation',N'012_tenant_data_isolation',N'013_tenant_membership_lifecycle');
SELECT @LedgerExists AS LedgerExists, @TenantMigrationCount AS TenantMigrationCount, @UserTableCount AS UserTableCount;
"@)
if ($migrationState.Count -ne 1) { throw 'Could not determine QC migration state.' }
$ledgerExists = [int]$migrationState[0].LedgerExists
$tenantMigrationCount = [int]$migrationState[0].TenantMigrationCount
$userTableCount = [int]$migrationState[0].UserTableCount
if ($tenantMigrationCount -notin @(0, 3)) { throw 'Tenant migrations 011-013 are partially installed. Resolve the migration ledger manually before SQL QC.' }
if ($ledgerExists -eq 1 -and $tenantMigrationCount -eq 0) {
    throw 'QC contains a legacy migration ledger without tenant migrations 011-013. Review legacy-workspace ownership and run Initialize-CreatorFlowDatabase.ps1 with explicit confirmation before CI; automatic backfill is refused.'
}
if ($ledgerExists -eq 0 -and $userTableCount -gt 0) {
    throw 'QC contains untracked tables without dbo.SchemaMigrations. Automatic baseline/adoption is refused.'
}

& (Join-Path $PSScriptRoot '../../database/Apply-Migrations.ps1') -SqlInstance $SqlInstance -Database $Database -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment -TrustServerCertificate:$TrustServerCertificate
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Applying SQL migrations 001-031 failed.' }
& (Join-Path $PSScriptRoot '../../database/Verify-Database.ps1') -SqlInstance $SqlInstance -Database $Database -RequireTenantMigrations -TrustServerCertificate:$TrustServerCertificate
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'Database schema verification failed after migrations 001-031.' }
Import-Module SqlServer -ErrorAction Stop
$result=Invoke-Sqlcmd -ServerInstance $SqlInstance -Database 'CreatorFlow_QC' -TrustServerCertificate:$TrustServerCertificate -Query "SELECT DB_NAME() AS DatabaseName, CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ProductVersion, 1 AS ConnectivityCheck;" -ErrorAction Stop
if ($result.DatabaseName -cne 'CreatorFlow_QC' -or $result.ConnectivityCheck -ne 1) { throw 'SQL connectivity smoke test failed.' }
Write-Host "SQL connectivity passed: $($result.DatabaseName), SQL $($result.ProductVersion)."
if (-not $SkipDotNetTests) {
 Push-Location $root
 try { dotnet test CreatorFlow.sln --configuration Release; if ($LASTEXITCODE -ne 0) { throw "dotnet test failed: $LASTEXITCODE" } }
 finally { Pop-Location }
}
Write-Host 'CreatorFlow SQL QC completed.'
