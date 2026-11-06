-- 031: public TikTok share resolver queue and rights-gated render jobs.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.SharedLinkInboxItems',N'U') IS NULL OR OBJECT_ID(N'dbo.VideoAssets',N'U') IS NULL OR OBJECT_ID(N'dbo.VideoRenderJobs',N'U') IS NULL
    THROW 53101, '031 requires migrations 016 and 017.', 1;
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
 ALTER TABLE dbo.SharedLinkInboxItems ADD CONSTRAINT FK_SharedLinkInbox_ResolvedAsset FOREIGN KEY(ResolvedAssetId) REFERENCES dbo.VideoAssets(Id);
 ALTER TABLE dbo.SharedLinkInboxItems ADD CONSTRAINT CK_SharedLinkInbox_ResolverStatus CHECK(ResolverStatus IN(N'Pending',N'Resolving',N'NeedsReview',N'Failed',N'RightsConfirmed'));
 CREATE INDEX IX_SharedLinkInbox_ResolverQueue ON dbo.SharedLinkInboxItems(ResolverStatus,SharedAt) INCLUDE(TenantId,Platform,ResolverAttemptCount);
END;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_VideoRenderJobs_Status')
BEGIN
 ALTER TABLE dbo.VideoRenderJobs ADD CONSTRAINT CK_VideoRenderJobs_Status CHECK(Status IN(N'AwaitingRightsReview',N'Queued',N'Rendering',N'Encoding',N'Completed',N'Failed',N'Cancelled'));
END
ELSE
BEGIN
 ALTER TABLE dbo.VideoRenderJobs DROP CONSTRAINT CK_VideoRenderJobs_Status;
 ALTER TABLE dbo.VideoRenderJobs ADD CONSTRAINT CK_VideoRenderJobs_Status CHECK(Status IN(N'AwaitingRightsReview',N'Queued',N'Rendering',N'Encoding',N'Completed',N'Failed',N'Cancelled'));
END;
COMMIT TRANSACTION;
