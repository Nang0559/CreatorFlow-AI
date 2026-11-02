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
if ($RequireTenantMigrations) { $prefixes += @('011_', '012_', '013_', '016_', '017_', '018_', '019_', '020_') }
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
if ($RequireTenantMigrations) { $requiredTables += @('Tenants', 'TenantMemberships', 'TenantInvitations', 'TenantSecurityAudit', 'SharedLinkInboxItems', 'VideoAssets', 'VideoRenderJobs') }
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
        'CreatorStyleProfileVersions', 'SharedLinkInboxItems', 'VideoAssets', 'VideoRenderJobs'
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
    $relationshipCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT fk.name
FROM sys.foreign_keys fk
WHERE fk.name IN (N'FK_VideoRenderJobs_Tenant_Asset', N'FK_SharedLinkInboxItems_Tenant_Channel')
  AND (fk.is_disabled=1 OR fk.is_not_trusted=1);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_VideoRenderJobs_Tenant_Asset' AND is_disabled=0 AND is_not_trusted=0)
    SELECT N'FK_VideoRenderJobs_Tenant_Asset' AS name;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SharedLinkInboxItems_Tenant_Channel' AND is_disabled=0 AND is_not_trusted=0)
    SELECT N'FK_SharedLinkInboxItems_Tenant_Channel' AS name;
"@)
    if ($relationshipCheck.Count -gt 0) { throw 'Same-tenant foreign keys are missing, disabled, or untrusted.' }
    $businessRelationshipCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT expected.ConstraintName
FROM (VALUES
 (N'FK_StyleExperiments_Tenant_ContentProject'),
 (N'FK_StyleExperiments_Tenant_WinningVariant'),
 (N'FK_StyleExperimentVariants_Tenant_Experiment'),
 (N'FK_StyleVariantMetricObservations_Tenant_Variant'),
 (N'FK_CreatorStyleEvidence_Tenant_Profile'),
 (N'FK_CreatorStyleEvidence_Tenant_Experiment'),
 (N'FK_CreatorStyleEvidence_Tenant_WinningVariant'),
 (N'FK_StyleLearningProposals_Tenant_Profile'),
 (N'FK_CreatorStyleProfileVersions_Tenant_Profile'),
 (N'FK_CreatorStyleProfileVersions_Tenant_Proposal'),
 (N'FK_TrendSelectionFeedback_Tenant_Candidate'),
 (N'FK_TrendCandidateOutcomeObservations_Tenant_Feedback'),
 (N'FK_DouyinVideoCandidates_Tenant_Channel'),
 (N'FK_DouyinVideoCandidates_Tenant_Topic'),
 (N'FK_VietnamTrendMetricSnapshots_Tenant_Signal')
) expected(ConstraintName)
LEFT JOIN sys.foreign_keys fk ON fk.name=expected.ConstraintName AND fk.is_disabled=0 AND fk.is_not_trusted=0
WHERE fk.object_id IS NULL;
"@)
    if ($businessRelationshipCheck.Count -gt 0) { throw 'One or more core business relationships are missing a trusted same-tenant foreign key.' }
    $uniqueCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT expected.IndexName
FROM (VALUES
 (N'UX_DouyinSourceChannels_Tenant_SecUserId'),
 (N'UX_DouyinVideoCandidates_Tenant_VideoId'),
 (N'UX_CreatorStyleProfiles_Tenant_ProfileKey'),
 (N'UX_VietnamTrendSignals_Tenant_External')
) expected(IndexName)
LEFT JOIN sys.indexes i ON i.name=expected.IndexName AND i.is_unique=1 AND i.is_disabled=0
WHERE i.index_id IS NULL;
"@)
    if ($uniqueCheck.Count -gt 0) { throw 'Workspace-scoped unique indexes are missing or disabled.' }
    $tenant = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT COUNT(*) AS TenantCount FROM dbo.Tenants WHERE Id='00000000-0000-4000-8000-000000000001' AND Slug=N'legacy-workspace';")
    if ($tenant[0].TenantCount -ne 1) { throw 'The explicit legacy-workspace tenant is missing.' }
}
Write-Host "Database verification PASSED: $Database; $($files.Count) migrations; $($requiredTables.Count) required tables."
