-- 029: tenant-scope all legacy Discovery 014/015 entities and close cross-tenant FK paths.
-- Existing global Discovery data is explicitly assigned to the legacy workspace.
-- Runtime consumers must always filter by TenantId; this migration supplies relational isolation,
-- not permission to expose these tables without tenant-aware queries.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @LegacyTenantId uniqueidentifier='00000000-0000-4000-8000-000000000001';
IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id=@LegacyTenantId AND Slug=N'legacy-workspace')
    THROW 52901, '029 requires the explicitly provisioned legacy-workspace tenant.', 1;

DECLARE @Tables TABLE (TableName sysname NOT NULL PRIMARY KEY);
INSERT @Tables VALUES
(N'DiscoveryTopicProfiles'),
(N'DiscoveryTopicRunHistory'),
(N'DiscoveryRuns'),
(N'DiscoverySources'),
(N'DiscoveryCandidates'),
(N'DiscoveryMetricSnapshots'),
(N'DiscoveryRankings'),
(N'DiscoveryPriorityLinks'),
(N'DiscoveryFeedback'),
(N'DiscoveryModelEvaluations'),
(N'DiscoveryFeedbackEvents'),
(N'DiscoveryCandidateFeatureSnapshots'),
(N'DiscoveryLearningInsights'),
(N'DiscoveryPreferenceRules'),
(N'DiscoveryLearningModelVersions');

DECLARE @Table sysname, @Sql nvarchar(max);
DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @Tables;
OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @Table;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID(N'dbo.'+@Table,N'U') IS NULL
        THROW 52902, 'A required Discovery 014/015 table is missing.', 1;

    IF COL_LENGTH(N'dbo.'+@Table,N'TenantId') IS NULL
    BEGIN
        SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Table)+N' ADD TenantId uniqueidentifier NOT NULL CONSTRAINT '
            +QUOTENAME(N'DF_'+@Table+'_TenantId')+N' DEFAULT (''00000000-0000-4000-8000-000000000001'') WITH VALUES;';
        EXEC sys.sp_executesql @Sql;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.'+@Table)
                   AND referenced_object_id=OBJECT_ID(N'dbo.Tenants')
                   AND name=N'FK_'+@Table+'_Tenants')
    BEGIN
        SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Table)+N' WITH CHECK ADD CONSTRAINT '
            +QUOTENAME(N'FK_'+@Table+'_Tenants')+N' FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id);'
            +N' ALTER TABLE dbo.'+QUOTENAME(@Table)+N' CHECK CONSTRAINT '+QUOTENAME(N'FK_'+@Table+'_Tenants')+N';';
        EXEC sys.sp_executesql @Sql;
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@Table)
                   AND name=N'UX_'+@Table+'_Tenant_Id')
    BEGIN
        SET @Sql=N'CREATE UNIQUE INDEX '+QUOTENAME(N'UX_'+@Table+'_Tenant_Id')
            +N' ON dbo.'+QUOTENAME(@Table)+N'(TenantId,Id);';
        EXEC sys.sp_executesql @Sql;
    END;
    FETCH NEXT FROM table_cursor INTO @Table;
END;
CLOSE table_cursor;
DEALLOCATE table_cursor;

-- The scheduler tables were tenant-scoped by migration 026; verify their composite keys exist.
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'TenantId') IS NULL
   OR COL_LENGTH(N'dbo.DiscoveryTopicRunHistory',N'TenantId') IS NULL
    THROW 52903, 'Discovery topic scheduler is not tenant-scoped; apply migration 026 first.', 1;

-- Replace globally unique business keys with tenant-scoped keys.
DECLARE @Drop TABLE (TableName sysname, ConstraintName sysname, IndexName sysname, CreateIndex nvarchar(max));
INSERT @Drop VALUES
(N'DiscoveryRuns',N'UQ_DiscoveryRuns_RunKey',NULL,N'CREATE UNIQUE INDEX UX_DiscoveryRuns_Tenant_RunKey ON dbo.DiscoveryRuns(TenantId,RunKey);'),
(N'DiscoveryTopicProfiles',N'UQ_DiscoveryTopicProfiles_Topic',NULL,N'CREATE UNIQUE INDEX UX_DiscoveryTopicProfiles_Tenant_Topic ON dbo.DiscoveryTopicProfiles(TenantId,ContentTopicId);'),
(N'DiscoverySources',N'UQ_DiscoverySources_Provider',NULL,N'CREATE UNIQUE INDEX UX_DiscoverySources_Tenant_Provider ON dbo.DiscoverySources(TenantId,Platform,Provider);'),
(N'DiscoveryCandidates',N'UQ_DiscoveryCandidates_Platform_Kind_External',NULL,N'CREATE UNIQUE INDEX UX_DiscoveryCandidates_Tenant_External ON dbo.DiscoveryCandidates(TenantId,Platform,CandidateKind,ExternalId);'),
(N'DiscoveryPriorityLinks',N'UQ_DiscoveryPriorityLinks_Url',NULL,N'CREATE UNIQUE INDEX UX_DiscoveryPriorityLinks_Tenant_Url ON dbo.DiscoveryPriorityLinks(TenantId,CanonicalUrl);'),
(N'DiscoveryLearningModelVersions',N'UQ_DiscoveryLearningModelVersions_Version',NULL,N'CREATE UNIQUE INDEX UX_DiscoveryLearningModelVersions_Tenant_Version ON dbo.DiscoveryLearningModelVersions(TenantId,ModelVersion);');

DECLARE @Constraint sysname, @Create nvarchar(max);
DECLARE unique_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName,ConstraintName,CreateIndex FROM @Drop;
OPEN unique_cursor;
FETCH NEXT FROM unique_cursor INTO @Table,@Constraint,@Create;
WHILE @@FETCH_STATUS=0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.'+@Table) AND name=@Constraint)
    BEGIN
        SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Table)+N' DROP CONSTRAINT '+QUOTENAME(@Constraint)+N';';
        EXEC sys.sp_executesql @Sql;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+@Table)
                   AND name=CASE @Table
                     WHEN N'DiscoveryRuns' THEN N'UX_DiscoveryRuns_Tenant_RunKey'
                     WHEN N'DiscoveryTopicProfiles' THEN N'UX_DiscoveryTopicProfiles_Tenant_Topic'
                     WHEN N'DiscoverySources' THEN N'UX_DiscoverySources_Tenant_Provider'
                     WHEN N'DiscoveryCandidates' THEN N'UX_DiscoveryCandidates_Tenant_External'
                     WHEN N'DiscoveryPriorityLinks' THEN N'UX_DiscoveryPriorityLinks_Tenant_Url'
                     ELSE N'UX_DiscoveryLearningModelVersions_Tenant_Version' END)
        EXEC sys.sp_executesql @Create;
    FETCH NEXT FROM unique_cursor INTO @Table,@Constraint,@Create;
END;
CLOSE unique_cursor;
DEALLOCATE unique_cursor;

-- Replace single-column relationships with tenant-composite relationships.
DECLARE @Relationships TABLE
(
    ChildTable sysname NOT NULL, OldConstraint sysname NOT NULL,
    NewConstraint sysname NOT NULL, ChildColumn sysname NOT NULL,
    ParentTable sysname NOT NULL, ParentColumn sysname NOT NULL,
    DeleteAction nvarchar(20) NOT NULL
);
INSERT @Relationships VALUES
(N'DiscoveryCandidates',N'FK_DiscoveryCandidates_Source',N'FK_DiscoveryCandidates_Tenant_Source',N'SourceId',N'DiscoverySources',N'Id',N'NO ACTION'),
(N'DiscoveryMetricSnapshots',N'FK_DiscoveryMetricSnapshots_Candidate',N'FK_DiscoveryMetricSnapshots_Tenant_Candidate',N'CandidateId',N'DiscoveryCandidates',N'Id',N'CASCADE'),
(N'DiscoveryMetricSnapshots',N'FK_DiscoveryMetricSnapshots_Run',N'FK_DiscoveryMetricSnapshots_Tenant_Run',N'RunId',N'DiscoveryRuns',N'Id',N'NO ACTION'),
(N'DiscoveryRankings',N'FK_DiscoveryRankings_Candidate',N'FK_DiscoveryRankings_Tenant_Candidate',N'CandidateId',N'DiscoveryCandidates',N'Id',N'CASCADE'),
(N'DiscoveryRankings',N'FK_DiscoveryRankings_Run',N'FK_DiscoveryRankings_Tenant_Run',N'RunId',N'DiscoveryRuns',N'Id',N'NO ACTION'),
(N'DiscoveryPriorityLinks',N'FK_DiscoveryPriorityLinks_Candidate',N'FK_DiscoveryPriorityLinks_Tenant_Candidate',N'CandidateId',N'DiscoveryCandidates',N'Id',N'NO ACTION'),
(N'DiscoveryFeedback',N'FK_DiscoveryFeedback_Candidate',N'FK_DiscoveryFeedback_Tenant_Candidate',N'CandidateId',N'DiscoveryCandidates',N'Id',N'NO ACTION'),
(N'DiscoveryFeedbackEvents',N'FK_DiscoveryFeedbackEvents_Candidate',N'FK_DiscoveryFeedbackEvents_Tenant_Candidate',N'CandidateId',N'DiscoveryCandidates',N'Id',N'NO ACTION'),
(N'DiscoveryCandidateFeatureSnapshots',N'FK_DiscoveryCandidateFeatureSnapshots_Candidate',N'FK_DiscoveryCandidateFeatureSnapshots_Tenant_Candidate',N'CandidateId',N'DiscoveryCandidates',N'Id',N'CASCADE'),
(N'DiscoveryLearningInsights',N'FK_DiscoveryLearningInsights_Feedback',N'FK_DiscoveryLearningInsights_Tenant_Feedback',N'FeedbackEventId',N'DiscoveryFeedbackEvents',N'Id',N'CASCADE'),
(N'DiscoveryPreferenceRules',N'FK_DiscoveryPreferenceRules_Insight',N'FK_DiscoveryPreferenceRules_Tenant_Insight',N'SourceInsightId',N'DiscoveryLearningInsights',N'Id',N'NO ACTION');

DECLARE @Child sysname,@Old sysname,@New sysname,@ChildCol sysname,@Parent sysname,@ParentCol sysname,@Action nvarchar(20);
DECLARE relationship_cursor CURSOR LOCAL FAST_FORWARD FOR
 SELECT ChildTable,OldConstraint,NewConstraint,ChildColumn,ParentTable,ParentColumn,DeleteAction FROM @Relationships;
OPEN relationship_cursor;
FETCH NEXT FROM relationship_cursor INTO @Child,@Old,@New,@ChildCol,@Parent,@ParentCol,@Action;
WHILE @@FETCH_STATUS=0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.'+@Child) AND name=@Old)
    BEGIN
        SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Child)+N' DROP CONSTRAINT '+QUOTENAME(@Old)+N';';
        EXEC sys.sp_executesql @Sql;
    END;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.'+@Child) AND name=@New)
    BEGIN
        SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Child)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(@New)
            +N' FOREIGN KEY(TenantId,'+QUOTENAME(@ChildCol)+N') REFERENCES dbo.'+QUOTENAME(@Parent)
            +N'(TenantId,'+QUOTENAME(@ParentCol)+N') ON DELETE '+@Action+N';'
            +N' ALTER TABLE dbo.'+QUOTENAME(@Child)+N' CHECK CONSTRAINT '+QUOTENAME(@New)+N';';
        EXEC sys.sp_executesql @Sql;
    END;
    FETCH NEXT FROM relationship_cursor INTO @Child,@Old,@New,@ChildCol,@Parent,@ParentCol,@Action;
END;
CLOSE relationship_cursor;
DEALLOCATE relationship_cursor;

-- Topic profile/history links must also carry the same tenant through their parent keys.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DiscoveryTopicProfiles') AND name=N'UX_DiscoveryTopicProfiles_Tenant_Id')
    CREATE UNIQUE INDEX UX_DiscoveryTopicProfiles_Tenant_Id ON dbo.DiscoveryTopicProfiles(TenantId,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DiscoveryTopicRunHistory') AND name=N'UX_DiscoveryTopicRunHistory_Tenant_Id')
    CREATE UNIQUE INDEX UX_DiscoveryTopicRunHistory_Tenant_Id ON dbo.DiscoveryTopicRunHistory(TenantId,Id);
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DiscoveryTopicRunHistory') AND name=N'FK_DiscoveryTopicRunHistory_Profile')
    ALTER TABLE dbo.DiscoveryTopicRunHistory DROP CONSTRAINT FK_DiscoveryTopicRunHistory_Profile;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DiscoveryTopicRunHistory') AND name=N'FK_DiscoveryTopicRunHistory_Tenant_Profile')
BEGIN
    ALTER TABLE dbo.DiscoveryTopicRunHistory WITH CHECK ADD CONSTRAINT FK_DiscoveryTopicRunHistory_Tenant_Profile
        FOREIGN KEY(TenantId,TopicProfileId) REFERENCES dbo.DiscoveryTopicProfiles(TenantId,Id);
    ALTER TABLE dbo.DiscoveryTopicRunHistory CHECK CONSTRAINT FK_DiscoveryTopicRunHistory_Tenant_Profile;
END;


-- Remove migration-only defaults after the one-time legacy backfill.
-- TenantId remains NOT NULL and FK-constrained; future inserts must supply a real tenant.
DECLARE @DefaultName sysname;
DECLARE default_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT dc.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns col
        ON col.object_id=dc.parent_object_id AND col.column_id=dc.parent_column_id
    WHERE dc.parent_object_id IN
    (
        SELECT OBJECT_ID(N'dbo.'+TableName) FROM @Tables
    )
    AND col.name=N'TenantId';
OPEN default_cursor;
FETCH NEXT FROM default_cursor INTO @DefaultName;
WHILE @@FETCH_STATUS=0
BEGIN
    DECLARE @DefaultTable sysname, @DefaultColumn sysname;
    SELECT @DefaultTable=OBJECT_NAME(dc.parent_object_id), @DefaultColumn=col.name
    FROM sys.default_constraints dc
    INNER JOIN sys.columns col
        ON col.object_id=dc.parent_object_id AND col.column_id=dc.parent_column_id
    WHERE dc.name=@DefaultName;
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@DefaultTable)+N' DROP CONSTRAINT '+QUOTENAME(@DefaultName)+N';';
    EXEC sys.sp_executesql @Sql;
    FETCH NEXT FROM default_cursor INTO @DefaultName;
END;
CLOSE default_cursor;
DEALLOCATE default_cursor;

COMMIT TRANSACTION;
