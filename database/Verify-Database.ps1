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
    $integrityCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @ExpectedForeignKeys TABLE
(
    ConstraintName sysname NOT NULL,
    ChildTable sysname NOT NULL,
    ChildColumn sysname NOT NULL,
    ParentTable sysname NOT NULL,
    DeleteAction nvarchar(20) NOT NULL
);
INSERT @ExpectedForeignKeys VALUES
(N'FK_VideoRenderJobs_Tenant_Asset',N'VideoRenderJobs',N'AssetId',N'VideoAssets',N'NO_ACTION'),
(N'FK_SharedLinkInboxItems_Tenant_Channel',N'SharedLinkInboxItems',N'LinkedChannelId',N'DouyinSourceChannels',N'NO_ACTION'),
(N'FK_StyleExperiments_Tenant_ContentProject',N'StyleExperiments',N'ContentProjectId',N'ContentProjects',N'NO_ACTION'),
(N'FK_StyleExperiments_Tenant_WinningVariant',N'StyleExperiments',N'WinningVariantId',N'StyleExperimentVariants',N'NO_ACTION'),
(N'FK_StyleExperimentVariants_Tenant_Experiment',N'StyleExperimentVariants',N'StyleExperimentId',N'StyleExperiments',N'CASCADE'),
(N'FK_StyleVariantMetricObservations_Tenant_Variant',N'StyleVariantMetricObservations',N'StyleExperimentVariantId',N'StyleExperimentVariants',N'CASCADE'),
(N'FK_CreatorStyleEvidence_Tenant_Profile',N'CreatorStyleEvidence',N'CreatorStyleProfileId',N'CreatorStyleProfiles',N'CASCADE'),
(N'FK_CreatorStyleEvidence_Tenant_Experiment',N'CreatorStyleEvidence',N'StyleExperimentId',N'StyleExperiments',N'NO_ACTION'),
(N'FK_CreatorStyleEvidence_Tenant_WinningVariant',N'CreatorStyleEvidence',N'WinningVariantId',N'StyleExperimentVariants',N'NO_ACTION'),
(N'FK_StyleLearningProposals_Tenant_Profile',N'StyleLearningProposals',N'CreatorStyleProfileId',N'CreatorStyleProfiles',N'CASCADE'),
(N'FK_CreatorStyleProfileVersions_Tenant_Profile',N'CreatorStyleProfileVersions',N'CreatorStyleProfileId',N'CreatorStyleProfiles',N'CASCADE'),
(N'FK_CreatorStyleProfileVersions_Tenant_Proposal',N'CreatorStyleProfileVersions',N'ApprovedProposalId',N'StyleLearningProposals',N'NO_ACTION'),
(N'FK_TrendSelectionFeedback_Tenant_Candidate',N'TrendSelectionFeedback',N'DouyinVideoCandidateId',N'DouyinVideoCandidates',N'NO_ACTION'),
(N'FK_TrendCandidateOutcomeObservations_Tenant_Feedback',N'TrendCandidateOutcomeObservations',N'FeedbackId',N'TrendSelectionFeedback',N'NO_ACTION'),
(N'FK_DouyinVideoCandidates_Tenant_Channel',N'DouyinVideoCandidates',N'SourceChannelId',N'DouyinSourceChannels',N'NO_ACTION'),
(N'FK_DouyinVideoCandidates_Tenant_Topic',N'DouyinVideoCandidates',N'ContentTopicId',N'ContentTopics',N'NO_ACTION'),
(N'FK_VietnamTrendMetricSnapshots_Tenant_Signal',N'VietnamTrendMetricSnapshots',N'TrendSignalId',N'VietnamTrendSignals',N'CASCADE');

SELECT N'FK ' + e.ConstraintName AS Problem
FROM @ExpectedForeignKeys e
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys fk
    WHERE fk.name=e.ConstraintName
      AND fk.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.ChildTable))
      AND fk.referenced_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.ParentTable))
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
      AND fk.delete_referential_action_desc=e.DeleteAction
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id)=2
      AND EXISTS
      (
          SELECT 1
          FROM sys.foreign_key_columns fkc
          JOIN sys.columns childColumn ON childColumn.object_id=fkc.parent_object_id AND childColumn.column_id=fkc.parent_column_id
          JOIN sys.columns parentColumn ON parentColumn.object_id=fkc.referenced_object_id AND parentColumn.column_id=fkc.referenced_column_id
          WHERE fkc.constraint_object_id=fk.object_id AND fkc.constraint_column_id=1
            AND childColumn.name=N'TenantId' AND parentColumn.name=N'TenantId'
      )
      AND EXISTS
      (
          SELECT 1
          FROM sys.foreign_key_columns fkc
          JOIN sys.columns childColumn ON childColumn.object_id=fkc.parent_object_id AND childColumn.column_id=fkc.parent_column_id
          JOIN sys.columns parentColumn ON parentColumn.object_id=fkc.referenced_object_id AND parentColumn.column_id=fkc.referenced_column_id
          WHERE fkc.constraint_object_id=fk.object_id AND fkc.constraint_column_id=2
            AND childColumn.name=e.ChildColumn AND parentColumn.name=N'Id'
      )
);

DECLARE @ExpectedIndexKeys TABLE
(
    IndexName sysname NOT NULL,
    TableName sysname NOT NULL,
    KeyOrdinal int NOT NULL,
    ColumnName sysname NOT NULL
);
INSERT @ExpectedIndexKeys VALUES
(N'UX_ContentProjects_Tenant_Id',N'ContentProjects',1,N'TenantId'),
(N'UX_ContentProjects_Tenant_Id',N'ContentProjects',2,N'Id'),
(N'UX_ContentTopics_Tenant_Id',N'ContentTopics',1,N'TenantId'),
(N'UX_ContentTopics_Tenant_Id',N'ContentTopics',2,N'Id'),
(N'UX_DouyinSourceChannels_Tenant_Id',N'DouyinSourceChannels',1,N'TenantId'),
(N'UX_DouyinSourceChannels_Tenant_Id',N'DouyinSourceChannels',2,N'Id'),
(N'UX_DouyinVideoCandidates_Tenant_Id',N'DouyinVideoCandidates',1,N'TenantId'),
(N'UX_DouyinVideoCandidates_Tenant_Id',N'DouyinVideoCandidates',2,N'Id'),
(N'UX_VietnamTrendSignals_Tenant_Id',N'VietnamTrendSignals',1,N'TenantId'),
(N'UX_VietnamTrendSignals_Tenant_Id',N'VietnamTrendSignals',2,N'Id'),
(N'UX_StyleExperiments_Tenant_Id',N'StyleExperiments',1,N'TenantId'),
(N'UX_StyleExperiments_Tenant_Id',N'StyleExperiments',2,N'Id'),
(N'UX_StyleExperimentVariants_Tenant_Id',N'StyleExperimentVariants',1,N'TenantId'),
(N'UX_StyleExperimentVariants_Tenant_Id',N'StyleExperimentVariants',2,N'Id'),
(N'UX_CreatorStyleProfiles_Tenant_Id',N'CreatorStyleProfiles',1,N'TenantId'),
(N'UX_CreatorStyleProfiles_Tenant_Id',N'CreatorStyleProfiles',2,N'Id'),
(N'UX_StyleLearningProposals_Tenant_Id',N'StyleLearningProposals',1,N'TenantId'),
(N'UX_StyleLearningProposals_Tenant_Id',N'StyleLearningProposals',2,N'Id'),
(N'UX_TrendSelectionFeedback_Tenant_Id',N'TrendSelectionFeedback',1,N'TenantId'),
(N'UX_TrendSelectionFeedback_Tenant_Id',N'TrendSelectionFeedback',2,N'Id'),
(N'UX_VideoAssets_Tenant_Id',N'VideoAssets',1,N'TenantId'),
(N'UX_VideoAssets_Tenant_Id',N'VideoAssets',2,N'Id'),
(N'UX_DouyinSourceChannels_Tenant_SecUserId',N'DouyinSourceChannels',1,N'TenantId'),
(N'UX_DouyinSourceChannels_Tenant_SecUserId',N'DouyinSourceChannels',2,N'DouyinSecUserId'),
(N'UX_DouyinVideoCandidates_Tenant_VideoId',N'DouyinVideoCandidates',1,N'TenantId'),
(N'UX_DouyinVideoCandidates_Tenant_VideoId',N'DouyinVideoCandidates',2,N'DouyinVideoId'),
(N'UX_CreatorStyleProfiles_Tenant_ProfileKey',N'CreatorStyleProfiles',1,N'TenantId'),
(N'UX_CreatorStyleProfiles_Tenant_ProfileKey',N'CreatorStyleProfiles',2,N'ProfileKey'),
(N'UX_VietnamTrendSignals_Tenant_External',N'VietnamTrendSignals',1,N'TenantId'),
(N'UX_VietnamTrendSignals_Tenant_External',N'VietnamTrendSignals',2,N'Platform'),
(N'UX_VietnamTrendSignals_Tenant_External',N'VietnamTrendSignals',3,N'Kind'),
(N'UX_VietnamTrendSignals_Tenant_External',N'VietnamTrendSignals',4,N'ExternalId');

SELECT N'INDEX ' + expected.IndexName AS Problem
FROM (SELECT DISTINCT IndexName,TableName FROM @ExpectedIndexKeys) expected
WHERE NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id=OBJECT_ID(N'dbo.'+QUOTENAME(expected.TableName))
      AND i.name=expected.IndexName
      AND i.is_unique=1 AND i.is_disabled=0 AND i.has_filter=0
      AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)
          =(SELECT COUNT(*) FROM @ExpectedIndexKeys k WHERE k.IndexName=expected.IndexName)
      AND NOT EXISTS
      (
          SELECT 1 FROM @ExpectedIndexKeys k
          WHERE k.IndexName=expected.IndexName
            AND NOT EXISTS
            (
                SELECT 1
                FROM sys.index_columns ic
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id
                  AND ic.key_ordinal=k.KeyOrdinal AND c.name=k.ColumnName
            )
      )
);
"@)
    if ($integrityCheck.Count -gt 0) { throw "Tenant relationship/index definition mismatch: $(($integrityCheck | ForEach-Object { $_.Problem }) -join ', ')" }
    $tenant = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT COUNT(*) AS TenantCount FROM dbo.Tenants WHERE Id='00000000-0000-4000-8000-000000000001' AND Slug=N'legacy-workspace';")
    if ($tenant[0].TenantCount -ne 1) { throw 'The explicit legacy-workspace tenant is missing.' }
}
Write-Host "Database verification PASSED: $Database; $($files.Count) migrations; $($requiredTables.Count) required tables."
