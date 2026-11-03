-- 021: durable, tenant-isolated channel sync state and permitted media download queue.
-- This migration creates queue primitives only. It does NOT enable a platform connector or
-- claim that Douyin/TikTok media can be fetched. A connector must be separately approved,
-- configured and registered before any scan/download worker can claim work.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.Tenants',N'U') IS NULL
    THROW 52101, 'Migration 011-013 tenant foundation is required.', 1;
IF OBJECT_ID(N'dbo.DouyinSourceChannels',N'U') IS NULL
    THROW 52102, 'Migration 003 Douyin channel table is required.', 1;
IF OBJECT_ID(N'dbo.DouyinVideoCandidates',N'U') IS NULL
    THROW 52103, 'Migration 003 video candidate table is required.', 1;

BEGIN TRANSACTION;

-- Composite keys make cross-workspace references impossible even if an application query is wrong.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DouyinSourceChannels') AND name=N'UX_DouyinSourceChannels_Tenant_Id')
    CREATE UNIQUE INDEX UX_DouyinSourceChannels_Tenant_Id ON dbo.DouyinSourceChannels(TenantId,Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DouyinVideoCandidates') AND name=N'UX_DouyinVideoCandidates_Tenant_Id')
    CREATE UNIQUE INDEX UX_DouyinVideoCandidates_Tenant_Id ON dbo.DouyinVideoCandidates(TenantId,Id);

IF OBJECT_ID(N'dbo.DiscoveryChannelSyncStates',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryChannelSyncStates(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryChannelSyncStates PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  ChannelId uniqueidentifier NOT NULL,
  IsEnabled bit NOT NULL CONSTRAINT DF_DiscoveryChannelSyncStates_Enabled DEFAULT 1,
  PollIntervalMinutes int NOT NULL CONSTRAINT DF_DiscoveryChannelSyncStates_Interval DEFAULT 30,
  NextScanAt datetimeoffset(7) NOT NULL,
  LastScanStartedAt datetimeoffset(7) NULL,
  LastScanCompletedAt datetimeoffset(7) NULL,
  ConsecutiveFailures int NOT NULL CONSTRAINT DF_DiscoveryChannelSyncStates_Failures DEFAULT 0,
  LastErrorCode nvarchar(80) NULL,
  UpdatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryChannelSyncStates_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_DiscoveryChannelSyncStates_Channel FOREIGN KEY(TenantId,ChannelId) REFERENCES dbo.DouyinSourceChannels(TenantId,Id) ON DELETE NO ACTION,
  CONSTRAINT UQ_DiscoveryChannelSyncStates_Tenant_Channel UNIQUE(TenantId,ChannelId),
  CONSTRAINT CK_DiscoveryChannelSyncStates_Interval CHECK(PollIntervalMinutes BETWEEN 15 AND 1440),
  CONSTRAINT CK_DiscoveryChannelSyncStates_Failures CHECK(ConsecutiveFailures BETWEEN 0 AND 100)
 );
 CREATE INDEX IX_DiscoveryChannelSyncStates_Due ON dbo.DiscoveryChannelSyncStates(IsEnabled,NextScanAt) INCLUDE(TenantId,ChannelId);
END;

IF OBJECT_ID(N'dbo.DiscoveryVideoDownloadJobs',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryVideoDownloadJobs(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryVideoDownloadJobs PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  CandidateId uniqueidentifier NOT NULL,
  Status nvarchar(20) NOT NULL CONSTRAINT DF_DiscoveryVideoDownloadJobs_Status DEFAULT N'AwaitingRightsReview',
  SourceUrl nvarchar(1200) NOT NULL,
  RightsBasis nvarchar(32) NULL,
  RightsReviewedAt datetimeoffset(7) NULL,
  RightsReviewedBy nvarchar(160) NULL,
  OutputRelativePath nvarchar(1000) NULL,
  ContentSha256 char(64) NULL,
  FileSizeBytes bigint NULL,
  AttemptCount int NOT NULL CONSTRAINT DF_DiscoveryVideoDownloadJobs_Attempts DEFAULT 0,
  AvailableAt datetimeoffset(7) NOT NULL,
  LockedAt datetimeoffset(7) NULL,
  CompletedAt datetimeoffset(7) NULL,
  LastErrorCode nvarchar(80) NULL,
  CreatedAt datetimeoffset(7) NOT NULL,
  UpdatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryVideoDownloadJobs_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_DiscoveryVideoDownloadJobs_Candidate FOREIGN KEY(TenantId,CandidateId) REFERENCES dbo.DouyinVideoCandidates(TenantId,Id) ON DELETE NO ACTION,
  CONSTRAINT UQ_DiscoveryVideoDownloadJobs_Tenant_Candidate UNIQUE(TenantId,CandidateId),
  CONSTRAINT CK_DiscoveryVideoDownloadJobs_Status CHECK(Status IN(N'AwaitingRightsReview',N'Queued',N'Downloading',N'Completed',N'Retry',N'Failed',N'Cancelled')),
  CONSTRAINT CK_DiscoveryVideoDownloadJobs_Rights CHECK(
    (Status IN(N'AwaitingRightsReview',N'Cancelled') AND RightsBasis IS NULL) OR
    (Status NOT IN(N'AwaitingRightsReview',N'Cancelled') AND RightsBasis IN(N'OwnerProvided',N'Licensed',N'OfficialPermittedApi',N'PublicDomain'))
  ),
  CONSTRAINT CK_DiscoveryVideoDownloadJobs_Attempts CHECK(AttemptCount BETWEEN 0 AND 10),
  CONSTRAINT CK_DiscoveryVideoDownloadJobs_Size CHECK(FileSizeBytes IS NULL OR FileSizeBytes>0),
  CONSTRAINT CK_DiscoveryVideoDownloadJobs_Hash CHECK(ContentSha256 IS NULL OR ContentSha256 NOT LIKE '%[^0-9a-fA-F]%')
 );
 CREATE INDEX IX_DiscoveryVideoDownloadJobs_Queue ON dbo.DiscoveryVideoDownloadJobs(Status,AvailableAt,CreatedAt) INCLUDE(TenantId,CandidateId,AttemptCount);
END;

COMMIT TRANSACTION;
