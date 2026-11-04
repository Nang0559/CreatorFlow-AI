-- 025: durable per-topic discovery scheduling and execution history.
-- Scheduling is persisted independently of the browser. This migration does not enable
-- any platform connector and does not claim that public TikTok/Douyin search is available.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.DiscoveryTopicProfiles',N'U') IS NULL
    THROW 52501, 'Migration 014 DiscoveryTopicProfiles is required.', 1;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'PlatformsJson') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD PlatformsJson nvarchar(200) NOT NULL
        CONSTRAINT DF_DiscoveryTopicProfiles_Platforms DEFAULT N'["TikTokVietnam"]';
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'NextRunAt') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD NextRunAt datetimeoffset(7) NULL;
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'LastRunAt') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD LastRunAt datetimeoffset(7) NULL;
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'LastRunStatus') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD LastRunStatus nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'LastErrorCode') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD LastErrorCode nvarchar(80) NULL;
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'LeaseUntil') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD LeaseUntil datetimeoffset(7) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DiscoveryTopicProfiles_PlatformsJson')
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD CONSTRAINT CK_DiscoveryTopicProfiles_PlatformsJson
        CHECK(ISJSON(PlatformsJson)=1);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DiscoveryTopicProfiles_LastRunStatus')
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD CONSTRAINT CK_DiscoveryTopicProfiles_LastRunStatus
        CHECK(LastRunStatus IS NULL OR LastRunStatus IN(N'Queued',N'Running',N'Completed',N'Partial',N'Failed',N'Skipped'));
IF OBJECT_ID(N'dbo.DiscoveryTopicRunHistory',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryTopicRunHistory(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryTopicRunHistory PRIMARY KEY,
  TopicProfileId uniqueidentifier NOT NULL,
  RunKey nvarchar(160) NOT NULL,
  TriggerKind nvarchar(16) NOT NULL,
  Status nvarchar(20) NOT NULL,
  PlatformsJson nvarchar(200) NOT NULL,
  StartedAt datetimeoffset(7) NOT NULL,
  CompletedAt datetimeoffset(7) NULL,
  CandidateCount int NOT NULL CONSTRAINT DF_DiscoveryTopicRunHistory_Candidates DEFAULT 0,
  ChannelCount int NOT NULL CONSTRAINT DF_DiscoveryTopicRunHistory_Channels DEFAULT 0,
  ErrorCode nvarchar(80) NULL,
  CONSTRAINT FK_DiscoveryTopicRunHistory_Profile FOREIGN KEY(TopicProfileId) REFERENCES dbo.DiscoveryTopicProfiles(Id) ON DELETE NO ACTION,
  CONSTRAINT UQ_DiscoveryTopicRunHistory_RunKey UNIQUE(RunKey),
  CONSTRAINT CK_DiscoveryTopicRunHistory_Trigger CHECK(TriggerKind IN(N'Schedule',N'Manual')),
  CONSTRAINT CK_DiscoveryTopicRunHistory_Status CHECK(Status IN(N'Queued',N'Running',N'Completed',N'Partial',N'Failed',N'Skipped')),
  CONSTRAINT CK_DiscoveryTopicRunHistory_Platforms CHECK(ISJSON(PlatformsJson)=1),
  CONSTRAINT CK_DiscoveryTopicRunHistory_Counts CHECK(CandidateCount>=0 AND ChannelCount>=0)
 );
 CREATE INDEX IX_DiscoveryTopicRunHistory_Profile_Started
  ON dbo.DiscoveryTopicRunHistory(TopicProfileId,StartedAt DESC);
END;
CREATE INDEX IX_DiscoveryTopicProfiles_Due
 ON dbo.DiscoveryTopicProfiles(IsEnabled,NextRunAt) INCLUDE(ContentTopicId,RefreshMinutes,LeaseUntil);
UPDATE dbo.DiscoveryTopicProfiles
 SET NextRunAt=DATEADD(minute,RefreshMinutes,SYSUTCDATETIME())
 WHERE IsEnabled=1 AND NextRunAt IS NULL;
COMMIT TRANSACTION;
