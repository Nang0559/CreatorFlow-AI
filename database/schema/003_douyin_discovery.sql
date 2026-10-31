-- Douyin discovery and content intake foundation.
-- Apply only to a dedicated CreatorFlow SQL Server database after 001/002.
IF OBJECT_ID(N'dbo.ContentTopics', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContentTopics
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContentTopics PRIMARY KEY,
        Name nvarchar(120) NOT NULL,
        Keywords nvarchar(4000) NOT NULL CONSTRAINT DF_ContentTopics_Keywords DEFAULT N'',
        IsActive bit NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL
    );
    CREATE INDEX IX_ContentTopics_Name ON dbo.ContentTopics(Name);
END;

IF OBJECT_ID(N'dbo.DouyinSourceChannels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DouyinSourceChannels
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DouyinSourceChannels PRIMARY KEY,
        DouyinSecUserId nvarchar(160) NOT NULL,
        DisplayName nvarchar(160) NOT NULL,
        ProfileUrl nvarchar(1000) NOT NULL,
        IsFollowed bit NOT NULL,
        FollowedAt datetimeoffset(7) NULL,
        LastCheckedAt datetimeoffset(7) NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL
    );
    CREATE UNIQUE INDEX UX_DouyinSourceChannels_SecUserId ON dbo.DouyinSourceChannels(DouyinSecUserId);
    CREATE INDEX IX_DouyinSourceChannels_Followed_LastChecked ON dbo.DouyinSourceChannels(IsFollowed, LastCheckedAt);
END;

IF OBJECT_ID(N'dbo.DouyinVideoCandidates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DouyinVideoCandidates
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DouyinVideoCandidates PRIMARY KEY,
        DouyinVideoId nvarchar(160) NOT NULL,
        SourceChannelId uniqueidentifier NULL,
        SourceUrl nvarchar(1000) NOT NULL,
        Title nvarchar(500) NOT NULL CONSTRAINT DF_DouyinVideoCandidates_Title DEFAULT N'',
        DiscoveryMethod nvarchar(32) NOT NULL,
        Status nvarchar(32) NOT NULL,
        PublishedAt datetimeoffset(7) NULL,
        DiscoveredAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_DouyinVideoCandidates_DiscoveryMethod
            CHECK (DiscoveryMethod IN (N'FollowedChannel', N'TopicExplore', N'Manual')),
        CONSTRAINT CK_DouyinVideoCandidates_Status
            CHECK (Status IN (N'Candidate', N'Selected', N'Rejected', N'QueuedForAdaptation', N'Adapted')),
        CONSTRAINT FK_DouyinVideoCandidates_SourceChannel
            FOREIGN KEY (SourceChannelId) REFERENCES dbo.DouyinSourceChannels(Id) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX UX_DouyinVideoCandidates_DouyinVideoId ON dbo.DouyinVideoCandidates(DouyinVideoId);
    CREATE INDEX IX_DouyinVideoCandidates_Status_DiscoveredAt ON dbo.DouyinVideoCandidates(Status, DiscoveredAt DESC);
END;
