-- 019: tenant-safe composite foreign keys across the core business model.
-- Prevents rows from one workspace referencing another workspace's records.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Parents TABLE(TableName sysname NOT NULL PRIMARY KEY);
INSERT @Parents(TableName) VALUES
(N'ContentProjects'),(N'ContentTopics'),(N'DouyinSourceChannels'),(N'DouyinVideoCandidates'),
(N'VietnamTrendSignals'),(N'StyleExperiments'),(N'StyleExperimentVariants'),
(N'CreatorStyleProfiles'),(N'StyleLearningProposals'),(N'TrendSelectionFeedback');

DECLARE @Parent sysname, @Sql nvarchar(max);
DECLARE parent_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @Parents;
OPEN parent_cursor;
FETCH NEXT FROM parent_cursor INTO @Parent;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID(N'dbo.'+QUOTENAME(@Parent),N'U') IS NULL
        THROW 51901, '019 is missing a required parent table.', 1;
    IF COL_LENGTH(N'dbo.'+@Parent,N'TenantId') IS NULL
        THROW 51902, '019 parent table is missing TenantId.', 1;
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+QUOTENAME(@Parent)) AND name=N'UX_'+@Parent+N'_Tenant_Id')
    BEGIN
        SET @Sql=N'CREATE UNIQUE INDEX '+QUOTENAME(N'UX_'+@Parent+N'_Tenant_Id')+
                 N' ON dbo.'+QUOTENAME(@Parent)+N'(TenantId,Id);';
        EXEC sys.sp_executesql @Sql;
    END;
    FETCH NEXT FROM parent_cursor INTO @Parent;
END;
CLOSE parent_cursor;
DEALLOCATE parent_cursor;

DECLARE @Relations TABLE(
    ChildTable sysname NOT NULL,
    ChildColumn sysname NOT NULL,
    ParentTable sysname NOT NULL,
    ConstraintName sysname NOT NULL,
    DeleteAction nvarchar(10) NOT NULL
);
INSERT @Relations VALUES
(N'StyleExperiments',N'ContentProjectId',N'ContentProjects',N'FK_StyleExperiments_Tenant_ContentProject',N'NO ACTION'),
(N'StyleExperiments',N'WinningVariantId',N'StyleExperimentVariants',N'FK_StyleExperiments_Tenant_WinningVariant',N'NO ACTION'),
(N'StyleExperimentVariants',N'StyleExperimentId',N'StyleExperiments',N'FK_StyleExperimentVariants_Tenant_Experiment',N'CASCADE'),
(N'StyleVariantMetricObservations',N'StyleExperimentVariantId',N'StyleExperimentVariants',N'FK_StyleVariantMetricObservations_Tenant_Variant',N'CASCADE'),
(N'CreatorStyleEvidence',N'CreatorStyleProfileId',N'CreatorStyleProfiles',N'FK_CreatorStyleEvidence_Tenant_Profile',N'CASCADE'),
(N'CreatorStyleEvidence',N'StyleExperimentId',N'StyleExperiments',N'FK_CreatorStyleEvidence_Tenant_Experiment',N'NO ACTION'),
(N'CreatorStyleEvidence',N'WinningVariantId',N'StyleExperimentVariants',N'FK_CreatorStyleEvidence_Tenant_WinningVariant',N'NO ACTION'),
(N'StyleLearningProposals',N'CreatorStyleProfileId',N'CreatorStyleProfiles',N'FK_StyleLearningProposals_Tenant_Profile',N'CASCADE'),
(N'CreatorStyleProfileVersions',N'CreatorStyleProfileId',N'CreatorStyleProfiles',N'FK_CreatorStyleProfileVersions_Tenant_Profile',N'CASCADE'),
(N'CreatorStyleProfileVersions',N'ApprovedProposalId',N'StyleLearningProposals',N'FK_CreatorStyleProfileVersions_Tenant_Proposal',N'NO ACTION'),
(N'TrendSelectionFeedback',N'DouyinVideoCandidateId',N'DouyinVideoCandidates',N'FK_TrendSelectionFeedback_Tenant_Candidate',N'NO ACTION'),
(N'TrendCandidateOutcomeObservations',N'FeedbackId',N'TrendSelectionFeedback',N'FK_TrendCandidateOutcomeObservations_Tenant_Feedback',N'NO ACTION'),
(N'DouyinVideoCandidates',N'SourceChannelId',N'DouyinSourceChannels',N'FK_DouyinVideoCandidates_Tenant_Channel',N'NO ACTION'),
(N'DouyinVideoCandidates',N'ContentTopicId',N'ContentTopics',N'FK_DouyinVideoCandidates_Tenant_Topic',N'NO ACTION'),
(N'VietnamTrendMetricSnapshots',N'TrendSignalId',N'VietnamTrendSignals',N'FK_VietnamTrendMetricSnapshots_Tenant_Signal',N'CASCADE');

DECLARE @Child sysname, @Column sysname, @Constraint sysname, @Action nvarchar(10);
DECLARE relation_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ChildTable,ChildColumn,ParentTable,ConstraintName,DeleteAction FROM @Relations;
OPEN relation_cursor;
FETCH NEXT FROM relation_cursor INTO @Child,@Column,@Parent,@Constraint,@Action;
WHILE @@FETCH_STATUS=0
BEGIN
    IF OBJECT_ID(N'dbo.'+QUOTENAME(@Child),N'U') IS NULL OR COL_LENGTH(N'dbo.'+@Child,N'TenantId') IS NULL OR COL_LENGTH(N'dbo.'+@Child,@Column) IS NULL
        THROW 51903, '019 child table or relationship column is missing.', 1;

    SELECT @Sql=STRING_AGG(
        N'ALTER TABLE dbo.'+QUOTENAME(@Child)+N' DROP CONSTRAINT '+QUOTENAME(fk.name)+N';',
        CHAR(10))
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(@Child))
      AND fk.referenced_object_id=OBJECT_ID(N'dbo.'+QUOTENAME(@Parent));
    IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;
    SET @Sql=NULL;

    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.'+QUOTENAME(@Child)) AND name=N'IX_'+@Child+N'_Tenant_'+@Column)
    BEGIN
        SET @Sql=N'CREATE INDEX '+QUOTENAME(N'IX_'+@Child+N'_Tenant_'+@Column)+
                 N' ON dbo.'+QUOTENAME(@Child)+N'(TenantId,'+QUOTENAME(@Column)+N');';
        EXEC sys.sp_executesql @Sql;
    END;

    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Child)+N' WITH CHECK ADD CONSTRAINT '+QUOTENAME(@Constraint)+
             N' FOREIGN KEY(TenantId,'+QUOTENAME(@Column)+N') REFERENCES dbo.'+QUOTENAME(@Parent)+
             N'(TenantId,Id) ON DELETE '+@Action+N';';
    EXEC sys.sp_executesql @Sql;
    SET @Sql=N'ALTER TABLE dbo.'+QUOTENAME(@Child)+N' CHECK CONSTRAINT '+QUOTENAME(@Constraint)+N';';
    EXEC sys.sp_executesql @Sql;

    FETCH NEXT FROM relation_cursor INTO @Child,@Column,@Parent,@Constraint,@Action;
END;
CLOSE relation_cursor;
DEALLOCATE relation_cursor;

COMMIT TRANSACTION;
