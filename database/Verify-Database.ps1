[CmdletBinding()]
param(
    [string]$SqlInstance = 'localhost',
    [ValidateSet('CreatorFlow_QC')]
    [string]$Database = 'CreatorFlow_QC',
    [switch]$RequireTenantMigrations,
    [bool]$TrustServerCertificate = $true
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$hostName = ($SqlInstance -split '\\')[0].Trim()
if (@('.', '(local)', 'localhost', $env:COMPUTERNAME) -notcontains $hostName) { throw "Refusing non-local SQL instance '$SqlInstance'." }
Import-Module SqlServer -ErrorAction Stop

$prefixes = @('001_', '002_', '003_', '004_', '005_', '006_', '007_', '008_', '009_', '010_', '014_', '015_')
if ($RequireTenantMigrations) { $prefixes += @('011_', '012_', '013_') }
$schemaPath = Join-Path $PSScriptRoot 'schema'
$files = @(Get-ChildItem -LiteralPath $schemaPath -File -Filter '*.sql' | Where-Object {
    $name = $_.Name
    @($prefixes | Where-Object { $name.StartsWith($_, [StringComparison]::Ordinal) }).Count -gt 0
} | Sort-Object Name)
if ($files.Count -ne $prefixes.Count) { throw 'Repository migration manifest is incomplete.' }

$identity = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT
    MAX(CASE WHEN name=N'CreatorFlowEnvironment' THEN CONVERT(nvarchar(128), value) END) AS EnvironmentTag,
    MAX(CASE WHEN name=N'CreatorFlowRepository' THEN CONVERT(nvarchar(128), value) END) AS RepositoryTag
FROM sys.extended_properties
WHERE class=0;
"@)
if ($identity.Count -ne 1 -or $identity[0].EnvironmentTag -cne 'QC' -or $identity[0].RepositoryTag -cne 'CreatorFlow-AI') { throw 'Database identity tags do not match CreatorFlow-AI QC.' }
$ledger = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT CASE WHEN OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL THEN 0 ELSE 1 END AS ExistsFlag;")
if ($ledger.Count -ne 1 -or $ledger[0].ExistsFlag -ne 1) { throw 'dbo.SchemaMigrations is missing. Run database/Apply-Migrations.ps1 first.' }

$requiredTables = @(
    'ContentProjects', 'ContentTopics', 'DouyinSourceChannels', 'DouyinVideoCandidates',
    'VietnamTrendSignals', 'VietnamTrendMetricSnapshots', 'StyleExperiments',
    'StyleExperimentVariants', 'CreatorStyleProfiles', 'CreatorStyleEvidence',
    'StyleVariantMetricObservations', 'StyleLearningProposals', 'CreatorStyleProfileVersions',
    'TrendSelectionFeedback', 'TrendCandidateOutcomeObservations',
    'DiscoveryRuns', 'DiscoveryTopicProfiles', 'DiscoverySources', 'DiscoveryCandidates',
    'DiscoveryMetricSnapshots', 'DiscoveryRankings', 'DiscoveryPriorityLinks',
    'DiscoveryFeedback', 'DiscoveryModelEvaluations', 'DiscoveryFeedbackEvents',
    'DiscoveryCandidateFeatureSnapshots', 'DiscoveryLearningInsights',
    'DiscoveryPreferenceRules', 'DiscoveryLearningModelVersions'
)
if ($RequireTenantMigrations) { $requiredTables += @('Tenants', 'TenantMemberships', 'TenantInvitations', 'TenantSecurityAudit') }
$tableValues = ($requiredTables | ForEach-Object { "(N'$_')" }) -join ','
$missingTables = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @Required TABLE (TableName sysname NOT NULL);
INSERT @Required(TableName) VALUES $tableValues;
SELECT r.TableName
FROM @Required r
WHERE OBJECT_ID(N'dbo.' + QUOTENAME(r.TableName), N'U') IS NULL;
"@)
if ($missingTables.Count -gt 0) { throw "Required schema tables are missing: $(($missingTables | ForEach-Object { $_.TableName }) -join ', ')" }

foreach ($file in $files) {
    $migrationId = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $safeId = $migrationId.Replace("'", "''")
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $row = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT ScriptName, ScriptSha256 FROM dbo.SchemaMigrations WHERE MigrationId=N'$safeId';")
    if ($row.Count -ne 1 -or $row[0].ScriptName -cne $file.Name -or $row[0].ScriptSha256 -cne $hash) { throw "Migration history missing or checksum mismatch: $($file.Name)" }
}

if ($RequireTenantMigrations) {
    $tenantTables = @(
        'ContentProjects', 'ContentTopics', 'DouyinSourceChannels', 'DouyinVideoCandidates',
        'VietnamTrendSignals', 'VietnamTrendMetricSnapshots', 'StyleExperiments',
        'StyleExperimentVariants', 'CreatorStyleProfiles', 'CreatorStyleEvidence',
        'StyleVariantMetricObservations', 'StyleLearningProposals',
        'TrendSelectionFeedback', 'TrendCandidateOutcomeObservations',
        'CreatorStyleProfileVersions'
    )
    $tenantValues = ($tenantTables | ForEach-Object { "(N'$_')" }) -join ','
    $tenantCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @TenantTables TABLE (TableName sysname NOT NULL);
INSERT @TenantTables(TableName) VALUES $tenantValues;
SELECT t.TableName
FROM @TenantTables t
WHERE COL_LENGTH(N'dbo.' + t.TableName, N'TenantId') IS NULL
   OR NOT EXISTS
      (SELECT 1 FROM sys.foreign_keys fk
       WHERE fk.parent_object_id=OBJECT_ID(N'dbo.' + QUOTENAME(t.TableName))
         AND fk.name=N'FK_' + t.TableName + N'_Tenants'
         AND fk.is_disabled=0 AND fk.is_not_trusted=0)
   OR NOT EXISTS
      (SELECT 1 FROM sys.indexes i
       WHERE i.object_id=OBJECT_ID(N'dbo.' + QUOTENAME(t.TableName))
         AND i.name=N'IX_' + t.TableName + N'_TenantId');
"@)
    if ($tenantCheck.Count -gt 0) { throw "Tenant isolation is incomplete (TenantId/FK/index missing or FK untrusted): $(($tenantCheck | ForEach-Object { $_.TableName }) -join ', ')" }
    $tenant = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT COUNT(*) AS TenantCount FROM dbo.Tenants WHERE Id='00000000-0000-4000-8000-000000000001' AND Slug=N'legacy-workspace';")
    if ($tenant[0].TenantCount -ne 1) { throw 'The explicit legacy-workspace tenant is missing.' }
}
Write-Host "Database verification PASSED: $Database; $($files.Count) migrations; $($requiredTables.Count) required tables."
