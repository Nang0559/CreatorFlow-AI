-- Add topic provenance to existing Douyin video candidates.
-- Apply only to a dedicated CreatorFlow SQL Server database after schema 003.
IF COL_LENGTH(N'dbo.DouyinVideoCandidates', N'ContentTopicId') IS NULL
BEGIN
    ALTER TABLE dbo.DouyinVideoCandidates ADD ContentTopicId uniqueidentifier NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_DouyinVideoCandidates_ContentTopics')
BEGIN
    ALTER TABLE dbo.DouyinVideoCandidates
        ADD CONSTRAINT FK_DouyinVideoCandidates_ContentTopics
        FOREIGN KEY (ContentTopicId) REFERENCES dbo.ContentTopics(Id) ON DELETE SET NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DouyinVideoCandidates_ContentTopicId' AND object_id = OBJECT_ID(N'dbo.DouyinVideoCandidates'))
BEGIN
    CREATE INDEX IX_DouyinVideoCandidates_ContentTopicId
        ON dbo.DouyinVideoCandidates(ContentTopicId, DiscoveredAt DESC);
END;
