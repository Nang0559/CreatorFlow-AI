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
if ($RequireTenantMigrations) { $prefixes += @('011_', '012_', '013_', '016_', '017_', '018_', '019_', '020_', '021_', '022_', '023_', '024_', '025_', '026_', '027_', '028_', '029_', '030_', '031_') }
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
    'DiscoveryPreferenceRules', 'DiscoveryLearningModelVersions', 'DiscoveryTopicRunHistory'
)
if ($RequireTenantMigrations) { $requiredTables += @('Tenants', 'TenantMemberships', 'TenantInvitations', 'TenantSecurityAudit', 'SharedLinkInboxItems', 'VideoAssets', 'VideoRenderJobs', 'CreatorFlowPlans') }
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

    $resolverCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
SELECT
    CASE WHEN COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolverStatus') IS NOT NULL
          AND COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolverError') IS NOT NULL
          AND COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolvedAssetId') IS NOT NULL
          AND COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolvedTitle') IS NOT NULL
          AND COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolverAttemptCount') IS NOT NULL
          AND COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolverUpdatedAt') IS NOT NULL
          AND COL_LENGTH(N'dbo.SharedLinkInboxItems',N'RightsConfirmedAt') IS NOT NULL
         THEN 1 ELSE 0 END AS ResolverColumnsPresent,
    CASE WHEN EXISTS
    (
        SELECT 1 FROM sys.foreign_keys fk
        WHERE fk.name=N'FK_SharedLinkInbox_Tenant_ResolvedAsset'
          AND fk.is_disabled=0 AND fk.is_not_trusted=0
          AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id)=2
          AND EXISTS
          (
              SELECT 1 FROM sys.foreign_key_columns fkc
              INNER JOIN sys.columns child ON child.object_id=fkc.parent_object_id AND child.column_id=fkc.parent_column_id
              INNER JOIN sys.columns parent ON parent.object_id=fkc.referenced_object_id AND parent.column_id=fkc.referenced_column_id
              WHERE fkc.constraint_object_id=fk.object_id AND child.name=N'TenantId' AND parent.name=N'TenantId'
          )
          AND EXISTS
          (
              SELECT 1 FROM sys.foreign_key_columns fkc
              INNER JOIN sys.columns child ON child.object_id=fkc.parent_object_id AND child.column_id=fkc.parent_column_id
              INNER JOIN sys.columns parent ON parent.object_id=fkc.referenced_object_id AND parent.column_id=fkc.referenced_column_id
              WHERE fkc.constraint_object_id=fk.object_id AND child.name=N'ResolvedAssetId' AND parent.name=N'Id'
          )
    )
         THEN 1 ELSE 0 END AS ResolvedAssetForeignKeyTrusted,
    CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name=N'CK_SharedLinkInbox_ResolverStatus' AND is_disabled=0 AND is_not_trusted=0)
         THEN 1 ELSE 0 END AS ResolverStatusConstraintTrusted,
    CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name=N'CK_VideoRenderJobs_Status' AND is_disabled=0 AND is_not_trusted=0
          AND definition LIKE N'%AwaitingRightsReview%')
         THEN 1 ELSE 0 END AS RightsReviewJobStatusAllowed;
"@)
    if ($resolverCheck.Count -ne 1 -or $resolverCheck[0].ResolverColumnsPresent -ne 1 -or
        $resolverCheck[0].ResolvedAssetForeignKeyTrusted -ne 1 -or
        $resolverCheck[0].ResolverStatusConstraintTrusted -ne 1 -or
        $resolverCheck[0].RightsReviewJobStatusAllowed -ne 1) {
        throw 'Public TikTok resolver migration 031 is missing or its rights-gating constraints are not trusted.'
    }
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
    WHERE fk.name COLLATE DATABASE_DEFAULT=e.ConstraintName
      AND fk.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.ChildTable))
      AND fk.referenced_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(e.ParentTable))
      AND fk.is_disabled=0 AND fk.is_not_trusted=0
      AND fk.delete_referential_action_desc COLLATE DATABASE_DEFAULT=e.DeleteAction
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id)=2
      AND EXISTS
      (
          SELECT 1
          FROM sys.foreign_key_columns fkc
          JOIN sys.columns childColumn ON childColumn.object_id=fkc.parent_object_id AND childColumn.column_id=fkc.parent_column_id
          JOIN sys.columns parentColumn ON parentColumn.object_id=fkc.referenced_object_id AND parentColumn.column_id=fkc.referenced_column_id
          WHERE fkc.constraint_object_id=fk.object_id AND fkc.constraint_column_id=1
            AND childColumn.name COLLATE DATABASE_DEFAULT=N'TenantId' AND parentColumn.name COLLATE DATABASE_DEFAULT=N'TenantId'
      )
      AND EXISTS
      (
          SELECT 1
          FROM sys.foreign_key_columns fkc
          JOIN sys.columns childColumn ON childColumn.object_id=fkc.parent_object_id AND childColumn.column_id=fkc.parent_column_id
          JOIN sys.columns parentColumn ON parentColumn.object_id=fkc.referenced_object_id AND parentColumn.column_id=fkc.referenced_column_id
          WHERE fkc.constraint_object_id=fk.object_id AND fkc.constraint_column_id=2
            AND childColumn.name COLLATE DATABASE_DEFAULT=e.ChildColumn AND parentColumn.name COLLATE DATABASE_DEFAULT=N'Id'
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
      AND i.name COLLATE DATABASE_DEFAULT=expected.IndexName
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
                  AND ic.key_ordinal=k.KeyOrdinal AND c.name COLLATE DATABASE_DEFAULT=k.ColumnName
            )
      )
);
"@)
    if ($integrityCheck.Count -gt 0) { throw "Tenant relationship/index definition mismatch: $(($integrityCheck | ForEach-Object { $_.Problem }) -join ', ')" }
    $discoveryTenantTables = @(
        'DiscoveryRuns','DiscoveryTopicProfiles','DiscoverySources','DiscoveryCandidates',
        'DiscoveryMetricSnapshots','DiscoveryRankings','DiscoveryPriorityLinks','DiscoveryFeedback',
        'DiscoveryModelEvaluations','DiscoveryFeedbackEvents','DiscoveryCandidateFeatureSnapshots',
        'DiscoveryLearningInsights','DiscoveryPreferenceRules','DiscoveryLearningModelVersions',
        'DiscoveryTopicRunHistory'
    )
    $discoveryTenantValues = ($discoveryTenantTables | ForEach-Object { "(N'$_')" }) -join ','
    $discoveryTenantCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @DiscoveryTables TABLE (TableName sysname NOT NULL);
INSERT @DiscoveryTables(TableName) VALUES $discoveryTenantValues;
SELECT t.TableName
FROM @DiscoveryTables t
WHERE COL_LENGTH(N'dbo.' + t.TableName, N'TenantId') IS NULL
   OR EXISTS
      (SELECT 1 FROM sys.default_constraints dc
       JOIN sys.columns c ON c.object_id=dc.parent_object_id AND c.column_id=dc.parent_column_id
       WHERE dc.parent_object_id=OBJECT_ID(N'dbo.' + QUOTENAME(t.TableName))
         AND c.name=N'TenantId')
   OR NOT EXISTS
      (SELECT 1 FROM sys.foreign_keys fk
       WHERE fk.parent_object_id=OBJECT_ID(N'dbo.' + QUOTENAME(t.TableName))
         AND fk.referenced_object_id=OBJECT_ID(N'dbo.Tenants')
         AND fk.is_disabled=0 AND fk.is_not_trusted=0
         AND EXISTS (SELECT 1 FROM sys.foreign_key_columns fkc
                     JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
                     WHERE fkc.constraint_object_id=fk.object_id AND c.name=N'TenantId'));
"@)
    if ($discoveryTenantCheck.Count -gt 0) { throw "Discovery tenant isolation is incomplete (014/015/025 tables missing TenantId or trusted tenant FK): $(($discoveryTenantCheck | ForEach-Object { $_.TableName }) -join ', ')" }

    $discoveryRelationships = @(
        'FK_DiscoveryCandidates_Tenant_Source',
        'FK_DiscoveryMetricSnapshots_Tenant_Candidate','FK_DiscoveryMetricSnapshots_Tenant_Run',
        'FK_DiscoveryRankings_Tenant_Candidate','FK_DiscoveryRankings_Tenant_Run',
        'FK_DiscoveryPriorityLinks_Tenant_Candidate','FK_DiscoveryFeedback_Tenant_Candidate',
        'FK_DiscoveryFeedbackEvents_Tenant_Candidate','FK_DiscoveryCandidateFeatureSnapshots_Tenant_Candidate',
        'FK_DiscoveryLearningInsights_Tenant_Feedback','FK_DiscoveryPreferenceRules_Tenant_Insight',
        'FK_DiscoveryTopicProfiles_Tenant_Topic','FK_DiscoveryTopicRunHistory_Tenant_Profile'
    )
    $relationshipValues = ($discoveryRelationships | ForEach-Object { "(N'$_')" }) -join ','
    $relationshipCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @Expected TABLE (ConstraintName sysname NOT NULL);
INSERT @Expected(ConstraintName) VALUES $relationshipValues;
SELECT e.ConstraintName
FROM @Expected e
WHERE NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys fk
    WHERE fk.name=e.ConstraintName AND fk.is_disabled=0 AND fk.is_not_trusted=0
      AND (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE fkc.constraint_object_id=fk.object_id)=2
      AND EXISTS
      (
        SELECT 1 FROM sys.foreign_key_columns fkc
        JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
        WHERE fkc.constraint_object_id=fk.object_id AND fkc.constraint_column_id=1 AND c.name=N'TenantId'
      )
      AND EXISTS
      (
        SELECT 1 FROM sys.foreign_key_columns fkc
        JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
        WHERE fkc.constraint_object_id=fk.object_id AND fkc.constraint_column_id=2
      )
);
"@)
    if ($relationshipCheck.Count -gt 0) { throw "Discovery tenant-composite relationships are missing or untrusted: $(($relationshipCheck | ForEach-Object { $_.ConstraintName }) -join ', ')" }

    $newTenantTables = @('TenantSubscriptions','TenantUsageEvents','TenantStorageObjects','AIProviders','DiscoveryChannelSyncStates','DiscoveryVideoDownloadJobs','DiscoveryTopicProfiles','DiscoveryTopicRunHistory','DiscoveryConnectorConfigurations')
    $newTenantValues = ($newTenantTables | ForEach-Object { "(N'$_')" }) -join ','
    $newTenantCheck = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query @"
DECLARE @NewTenantTables TABLE (TableName sysname NOT NULL);
INSERT @NewTenantTables(TableName) VALUES $newTenantValues;
SELECT t.TableName
FROM @NewTenantTables t
WHERE COL_LENGTH(N'dbo.' + t.TableName, N'TenantId') IS NULL
   OR NOT EXISTS
      (SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID(N'dbo.'+QUOTENAME(t.TableName)) AND c.name=N'TenantId' AND ic.key_ordinal=1 AND i.is_disabled=0)
   OR NOT EXISTS
      (SELECT 1 FROM sys.foreign_key_columns fkc
       JOIN sys.foreign_keys fk ON fk.object_id=fkc.constraint_object_id
       JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
       WHERE fk.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(t.TableName))
         AND c.name=N'TenantId' AND fk.is_disabled=0 AND fk.is_not_trusted=0);
"@)
    if ($newTenantCheck.Count -gt 0) { throw "New tenant-owned tables lack TenantId or a trusted TenantId foreign key: $(($newTenantCheck | ForEach-Object { $_.TableName }) -join ', ')" }

    $tenant = @(Invoke-Sqlcmd -ServerInstance $SqlInstance -Database $Database -TrustServerCertificate:$TrustServerCertificate -ErrorAction Stop -Query "SELECT COUNT(*) AS TenantCount FROM dbo.Tenants WHERE Id='00000000-0000-4000-8000-000000000001' AND Slug=N'legacy-workspace';")
    if ($tenant[0].TenantCount -ne 1) { throw 'The explicit legacy-workspace tenant is missing.' }
}
Write-Host "Database verification PASSED: $Database; $($files.Count) migrations; $($requiredTables.Count) required tables."
