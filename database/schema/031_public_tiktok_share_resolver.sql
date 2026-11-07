-- 031: public TikTok share resolver queue and rights-gated render jobs.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.SharedLinkInboxItems',N'U') IS NULL OR OBJECT_ID(N'dbo.VideoAssets',N'U') IS NULL OR OBJECT_ID(N'dbo.VideoRenderJobs',N'U') IS NULL
    THROW 53101, '031 requires migrations 016 and 017.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.VideoAssets') AND name=N'UX_VideoAssets_Tenant_Id')
    THROW 53102, '031 requires migration 018 same-tenant VideoAssets key.', 1;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.SharedLinkInboxItems',N'ResolverStatus') IS NULL
BEGIN
    ALTER TABLE dbo.SharedLinkInboxItems ADD
        ResolverStatus nvarchar(24) NOT NULL CONSTRAINT DF_SharedLinkInbox_ResolverStatus DEFAULT N'Pending',
        ResolverError nvarchar(1000) NULL,
        ResolvedAssetId uniqueidentifier NULL,
        ResolvedTitle nvarchar(180) NULL,
        ResolverAttemptCount tinyint NOT NULL CONSTRAINT DF_SharedLinkInbox_ResolverAttempts DEFAULT 0,
        ResolverUpdatedAt datetimeoffset(7) NULL,
        RightsConfirmedAt datetimeoffset(7) NULL;
END;

-- Use dynamic SQL for objects that reference columns added above. SQL Server compiles
-- static DDL against the pre-ALTER table shape when all statements share one batch.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_SharedLinkInbox_Tenant_ResolvedAsset')
    EXEC sys.sp_executesql N'ALTER TABLE dbo.SharedLinkInboxItems WITH CHECK ADD CONSTRAINT FK_SharedLinkInbox_Tenant_ResolvedAsset FOREIGN KEY(TenantId,ResolvedAssetId) REFERENCES dbo.VideoAssets(TenantId,Id) ON DELETE NO ACTION;';

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_SharedLinkInbox_ResolverStatus')
    EXEC sys.sp_executesql N'ALTER TABLE dbo.SharedLinkInboxItems ADD CONSTRAINT CK_SharedLinkInbox_ResolverStatus CHECK(ResolverStatus IN(N''Pending'',N''Resolving'',N''NeedsReview'',N''Failed'',N''RightsConfirmed''));';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.SharedLinkInboxItems') AND name=N'IX_SharedLinkInbox_ResolverQueue')
    EXEC sys.sp_executesql N'CREATE INDEX IX_SharedLinkInbox_ResolverQueue ON dbo.SharedLinkInboxItems(ResolverStatus,SharedAt) INCLUDE(TenantId,Platform,ResolverAttemptCount);';

IF EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE name=N'CK_VideoRenderJobs_Status'
      AND definition NOT LIKE N'%AwaitingRightsReview%'
)
    ALTER TABLE dbo.VideoRenderJobs DROP CONSTRAINT CK_VideoRenderJobs_Status;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name=N'CK_VideoRenderJobs_Status')
    ALTER TABLE dbo.VideoRenderJobs ADD CONSTRAINT CK_VideoRenderJobs_Status
        CHECK(Status IN(N'AwaitingRightsReview',N'Queued',N'Rendering',N'Encoding',N'Completed',N'Failed',N'Cancelled'));

COMMIT TRANSACTION;
