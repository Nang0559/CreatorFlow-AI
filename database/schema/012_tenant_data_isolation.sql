-- CreatorFlow tenant data isolation.
-- Migration 011 must be applied first. Existing single-workspace records are assigned to
-- the explicit legacy workspace below; no business rows are deleted or rewritten otherwise.
-- Review this ownership assumption before applying to a database that already contains
-- data belonging to more than one customer.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @LegacyTenantId uniqueidentifier = '00000000-0000-4000-8000-000000000001';

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @LegacyTenantId)
BEGIN
    INSERT dbo.Tenants (Id, Name, Slug, Status, CreatedAt, UpdatedAt)
    VALUES (@LegacyTenantId, N'Legacy workspace', N'legacy-workspace', N'Active',
            SYSUTCDATETIME(), SYSUTCDATETIME());
END;

DECLARE @Tables TABLE (TableName sysname NOT NULL PRIMARY KEY);
INSERT @Tables (TableName) VALUES
(N'ContentProjects'), (N'ContentTopics'), (N'DouyinSourceChannels'),
(N'DouyinVideoCandidates'), (N'TrendSelectionFeedback'),
(N'TrendCandidateOutcomeObservations'), (N'VietnamTrendSignals'),
(N'VietnamTrendMetricSnapshots'), (N'StyleExperiments'),
(N'StyleExperimentVariants'), (N'CreatorStyleProfiles'),
(N'CreatorStyleEvidence'), (N'StyleVariantMetricObservations'),
(N'StyleLearningProposals'), (N'CreatorStyleProfileVersions');

DECLARE @TableName sysname, @Sql nvarchar(max);
DECLARE tenant_tables CURSOR LOCAL FAST_FORWARD FOR SELECT TableName FROM @Tables;
OPEN tenant_tables;
FETCH NEXT FROM tenant_tables INTO @TableName;

WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + QUOTENAME(@TableName), N'U') IS NULL
        THROW 51200, 'Tenant migration prerequisite table is missing.', 1;

    IF COL_LENGTH(N'dbo.' + @TableName, N'TenantId') IS NULL
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName) + N' ADD TenantId uniqueidentifier NULL;';
        EXEC sys.sp_executesql @Sql;
    END;

    SET @Sql = N'UPDATE dbo.' + QUOTENAME(@TableName) +
               N' SET TenantId = @TenantId WHERE TenantId IS NULL;';
    EXEC sys.sp_executesql @Sql, N'@TenantId uniqueidentifier', @TenantId = @LegacyTenantId;

    SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName) + N' ALTER COLUMN TenantId uniqueidentifier NOT NULL;';
    EXEC sys.sp_executesql @Sql;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.' + @TableName)
          AND name = N'FK_' + @TableName + N'_Tenants'
    )
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@TableName) +
                   N' WITH CHECK ADD CONSTRAINT ' + QUOTENAME(N'FK_' + @TableName + N'_Tenants') +
                   N' FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION;';
        EXEC sys.sp_executesql @Sql;
    END;

    IF NOT EXISTS
    (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.' + @TableName)
          AND name = N'IX_' + @TableName + N'_TenantId'
    )
    BEGIN
        SET @Sql = N'CREATE INDEX ' + QUOTENAME(N'IX_' + @TableName + N'_TenantId') +
                   N' ON dbo.' + QUOTENAME(@TableName) + N'(TenantId);';
        EXEC sys.sp_executesql @Sql;
    END;

    FETCH NEXT FROM tenant_tables INTO @TableName;
END;

CLOSE tenant_tables;
DEALLOCATE tenant_tables;

COMMIT TRANSACTION;
