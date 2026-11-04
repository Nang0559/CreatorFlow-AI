-- 026: tenant-scope durable discovery schedules and run history.
-- Migration 025 introduced the scheduler tables before the tenant ownership columns
-- existed. Backfill ownership from the owning ContentTopic, then enforce composite FKs.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DiscoveryTopicProfiles',N'U') IS NULL
    THROW 52601, '026 requires dbo.DiscoveryTopicProfiles.', 1;
IF OBJECT_ID(N'dbo.DiscoveryTopicRunHistory',N'U') IS NULL
    THROW 52602, '026 requires dbo.DiscoveryTopicRunHistory.', 1;
IF COL_LENGTH(N'dbo.ContentTopics',N'TenantId') IS NULL
    THROW 52603, '026 requires tenant-scoped dbo.ContentTopics.', 1;

IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'TenantId') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD TenantId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.DiscoveryTopicRunHistory',N'TenantId') IS NULL
    ALTER TABLE dbo.DiscoveryTopicRunHistory ADD TenantId uniqueidentifier NULL;

UPDATE p
   SET TenantId=t.TenantId
  FROM dbo.DiscoveryTopicProfiles p
  JOIN dbo.ContentTopics t ON t.Id=p.ContentTopicId
 WHERE p.TenantId IS NULL;

UPDATE h
   SET TenantId=p.TenantId
  FROM dbo.DiscoveryTopicRunHistory h
  JOIN dbo.DiscoveryTopicProfiles p ON p.Id=h.TopicProfileId
 WHERE h.TenantId IS NULL;

IF EXISTS(SELECT 1 FROM dbo.DiscoveryTopicProfiles WHERE TenantId IS NULL)
    THROW 52604, '026 could not resolve tenant ownership for every topic profile.', 1;
IF EXISTS(SELECT 1 FROM dbo.DiscoveryTopicRunHistory WHERE TenantId IS NULL)
    THROW 52605, '026 could not resolve tenant ownership for every run history row.', 1;

ALTER TABLE dbo.DiscoveryTopicProfiles ALTER COLUMN TenantId uniqueidentifier NOT NULL;
ALTER TABLE dbo.DiscoveryTopicRunHistory ALTER COLUMN TenantId uniqueidentifier NOT NULL;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.ContentTopics') AND name=N'UX_ContentTopics_Tenant_Id')
    CREATE UNIQUE INDEX UX_ContentTopics_Tenant_Id ON dbo.ContentTopics(TenantId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DiscoveryTopicProfiles') AND name=N'UX_DiscoveryTopicProfiles_Tenant_Id')
    CREATE UNIQUE INDEX UX_DiscoveryTopicProfiles_Tenant_Id ON dbo.DiscoveryTopicProfiles(TenantId,Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DiscoveryTopicRunHistory') AND name=N'IX_DiscoveryTopicRunHistory_Tenant_Profile_Started')
    CREATE INDEX IX_DiscoveryTopicRunHistory_Tenant_Profile_Started
        ON dbo.DiscoveryTopicRunHistory(TenantId,TopicProfileId,StartedAt DESC);

IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DiscoveryTopicProfiles') AND name=N'FK_DiscoveryTopicProfiles_Topics')
    ALTER TABLE dbo.DiscoveryTopicProfiles DROP CONSTRAINT FK_DiscoveryTopicProfiles_Topics;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DiscoveryTopicProfiles') AND name=N'FK_DiscoveryTopicProfiles_Tenant_Topic')
    ALTER TABLE dbo.DiscoveryTopicProfiles WITH CHECK ADD CONSTRAINT FK_DiscoveryTopicProfiles_Tenant_Topic
        FOREIGN KEY(TenantId,ContentTopicId) REFERENCES dbo.ContentTopics(TenantId,Id) ON DELETE NO ACTION;
ALTER TABLE dbo.DiscoveryTopicProfiles CHECK CONSTRAINT FK_DiscoveryTopicProfiles_Tenant_Topic;

IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.DiscoveryTopicRunHistory') AND name=N'FK_DiscoveryTopicRunHistory_Tenant_Profile')
    ALTER TABLE dbo.DiscoveryTopicRunHistory WITH CHECK ADD CONSTRAINT FK_DiscoveryTopicRunHistory_Tenant_Profile
        FOREIGN KEY(TenantId,TopicProfileId) REFERENCES dbo.DiscoveryTopicProfiles(TenantId,Id) ON DELETE NO ACTION;
ALTER TABLE dbo.DiscoveryTopicRunHistory CHECK CONSTRAINT FK_DiscoveryTopicRunHistory_Tenant_Profile;

COMMIT TRANSACTION;
