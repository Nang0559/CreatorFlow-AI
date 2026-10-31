-- Vietnam-market trend observations and controlled creative style experiments.
-- Apply after 001-004 to the dedicated CreatorFlow database.
IF OBJECT_ID(N'dbo.VietnamTrendSignals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VietnamTrendSignals
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_VietnamTrendSignals PRIMARY KEY,
        Kind nvarchar(32) NOT NULL,
        Platform nvarchar(40) NOT NULL,
        ExternalId nvarchar(180) NOT NULL,
        Title nvarchar(500) NOT NULL CONSTRAINT DF_VietnamTrendSignals_Title DEFAULT N'',
        CreatorName nvarchar(200) NOT NULL CONSTRAINT DF_VietnamTrendSignals_CreatorName DEFAULT N'',
        SourceUrl nvarchar(1000) NOT NULL,
        Topic nvarchar(200) NULL,
        SoundName nvarchar(300) NULL,
        SoundArtist nvarchar(200) NULL,
        Views bigint NOT NULL CONSTRAINT DF_VietnamTrendSignals_Views DEFAULT 0,
        Likes bigint NOT NULL CONSTRAINT DF_VietnamTrendSignals_Likes DEFAULT 0,
        Comments bigint NOT NULL CONSTRAINT DF_VietnamTrendSignals_Comments DEFAULT 0,
        Shares bigint NOT NULL CONSTRAINT DF_VietnamTrendSignals_Shares DEFAULT 0,
        Saves bigint NOT NULL CONSTRAINT DF_VietnamTrendSignals_Saves DEFAULT 0,
        PublishedAt datetimeoffset(7) NULL,
        ObservedAt datetimeoffset(7) NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_VietnamTrendSignals_Kind CHECK (Kind IN (N'CreatorChannel', N'Video', N'Sound')),
        CONSTRAINT CK_VietnamTrendSignals_Platform CHECK (Platform IN (N'TikTokVietnam', N'YouTubeShortsVietnam', N'FacebookReelsVietnam', N'Other')),
        CONSTRAINT CK_VietnamTrendSignals_Metrics CHECK (Views >= 0 AND Likes >= 0 AND Comments >= 0 AND Shares >= 0 AND Saves >= 0)
    );
    CREATE UNIQUE INDEX UX_VietnamTrendSignals_Platform_Kind_ExternalId
        ON dbo.VietnamTrendSignals(Platform, Kind, ExternalId);
    CREATE INDEX IX_VietnamTrendSignals_Kind_ObservedAt
        ON dbo.VietnamTrendSignals(Kind, ObservedAt DESC);
END;

IF OBJECT_ID(N'dbo.StyleExperiments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StyleExperiments
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_StyleExperiments PRIMARY KEY,
        ContentProjectId uniqueidentifier NULL,
        Name nvarchar(160) NOT NULL,
        Hypothesis nvarchar(2000) NOT NULL,
        Status nvarchar(24) NOT NULL,
        WinningVariantId uniqueidentifier NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_StyleExperiments_Status CHECK (Status IN (N'Draft', N'Running', N'Evaluated', N'Archived')),
        CONSTRAINT FK_StyleExperiments_ContentProjects FOREIGN KEY (ContentProjectId)
            REFERENCES dbo.ContentProjects(Id) ON DELETE SET NULL
    );
    CREATE INDEX IX_StyleExperiments_Status_UpdatedAt ON dbo.StyleExperiments(Status, UpdatedAt DESC);
END;

IF OBJECT_ID(N'dbo.StyleExperimentVariants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StyleExperimentVariants
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_StyleExperimentVariants PRIMARY KEY,
        StyleExperimentId uniqueidentifier NOT NULL,
        Name nvarchar(120) NOT NULL,
        Hook nvarchar(1000) NOT NULL,
        SpeakingStyle nvarchar(1000) NOT NULL,
        EditPacing nvarchar(1000) NOT NULL,
        AudioApproach nvarchar(1000) NOT NULL,
        Script nvarchar(12000) NOT NULL,
        CreativeNotes nvarchar(4000) NULL,
        Status nvarchar(24) NOT NULL,
        Views bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Views DEFAULT 0,
        Likes bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Likes DEFAULT 0,
        Comments bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Comments DEFAULT 0,
        Shares bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Shares DEFAULT 0,
        Saves bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Saves DEFAULT 0,
        FollowsGained bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Follows DEFAULT 0,
        ProductClicks bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_ProductClicks DEFAULT 0,
        Orders bigint NOT NULL CONSTRAINT DF_StyleExperimentVariants_Orders DEFAULT 0,
        AverageWatchSeconds decimal(12,3) NULL,
        VideoDurationSeconds decimal(12,3) NULL,
        PublishedAt datetimeoffset(7) NULL,
        MeasuredAt datetimeoffset(7) NULL,
        CONSTRAINT FK_StyleExperimentVariants_StyleExperiments FOREIGN KEY (StyleExperimentId)
            REFERENCES dbo.StyleExperiments(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_StyleExperimentVariants_Experiment_Name UNIQUE (StyleExperimentId, Name),
        CONSTRAINT CK_StyleExperimentVariants_Status CHECK (Status IN (N'Draft', N'Ready', N'Published', N'Measured')),
        CONSTRAINT CK_StyleExperimentVariants_Metrics CHECK
            (Views >= 0 AND Likes >= 0 AND Comments >= 0 AND Shares >= 0 AND Saves >= 0
             AND FollowsGained >= 0 AND ProductClicks >= 0 AND Orders >= 0),
        CONSTRAINT CK_StyleExperimentVariants_WatchTime CHECK
            (AverageWatchSeconds IS NULL OR AverageWatchSeconds >= 0)
            AND (VideoDurationSeconds IS NULL OR VideoDurationSeconds >= 0)
            AND (AverageWatchSeconds IS NULL OR VideoDurationSeconds IS NULL OR AverageWatchSeconds <= VideoDurationSeconds)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StyleExperiments_WinningVariant')
BEGIN
    ALTER TABLE dbo.StyleExperiments ADD CONSTRAINT FK_StyleExperiments_WinningVariant
        FOREIGN KEY (WinningVariantId) REFERENCES dbo.StyleExperimentVariants(Id);
END;
