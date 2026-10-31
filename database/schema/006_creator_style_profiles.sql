-- Creator-specific style identity and auditable evidence from confirmed experiment winners.
IF OBJECT_ID(N'dbo.CreatorStyleProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CreatorStyleProfiles
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CreatorStyleProfiles PRIMARY KEY,
        ProfileKey nvarchar(160) NOT NULL,
        DisplayName nvarchar(160) NOT NULL,
        Positioning nvarchar(1000) NOT NULL CONSTRAINT DF_CreatorStyleProfiles_Positioning DEFAULT N'',
        StyleSummary nvarchar(4000) NOT NULL CONSTRAINT DF_CreatorStyleProfiles_StyleSummary DEFAULT N'',
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL
    );
    CREATE UNIQUE INDEX UX_CreatorStyleProfiles_ProfileKey ON dbo.CreatorStyleProfiles(ProfileKey);
END;

IF OBJECT_ID(N'dbo.CreatorStyleEvidence', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CreatorStyleEvidence
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CreatorStyleEvidence PRIMARY KEY,
        CreatorStyleProfileId uniqueidentifier NOT NULL,
        StyleExperimentId uniqueidentifier NOT NULL,
        WinningVariantId uniqueidentifier NOT NULL,
        ExperimentName nvarchar(160) NOT NULL,
        VariantName nvarchar(120) NOT NULL,
        Hook nvarchar(1000) NOT NULL,
        SpeakingStyle nvarchar(1000) NOT NULL,
        EditPacing nvarchar(1000) NOT NULL,
        AudioApproach nvarchar(1000) NOT NULL,
        Views bigint NOT NULL,
        Likes bigint NOT NULL,
        Comments bigint NOT NULL,
        Shares bigint NOT NULL,
        Saves bigint NOT NULL,
        FollowsGained bigint NOT NULL,
        ProductClicks bigint NOT NULL,
        Orders bigint NOT NULL,
        AverageWatchSeconds decimal(12,3) NULL,
        VideoDurationSeconds decimal(12,3) NULL,
        LearnedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT FK_CreatorStyleEvidence_Profiles FOREIGN KEY (CreatorStyleProfileId)
            REFERENCES dbo.CreatorStyleProfiles(Id) ON DELETE CASCADE,
        CONSTRAINT FK_CreatorStyleEvidence_Experiments FOREIGN KEY (StyleExperimentId)
            REFERENCES dbo.StyleExperiments(Id),
        CONSTRAINT FK_CreatorStyleEvidence_WinningVariants FOREIGN KEY (WinningVariantId)
            REFERENCES dbo.StyleExperimentVariants(Id),
        CONSTRAINT CK_CreatorStyleEvidence_Metrics CHECK
            (Views >= 0 AND Likes >= 0 AND Comments >= 0 AND Shares >= 0 AND Saves >= 0
             AND FollowsGained >= 0 AND ProductClicks >= 0 AND Orders >= 0),
        CONSTRAINT CK_CreatorStyleEvidence_WatchTime CHECK
            ((AverageWatchSeconds IS NULL OR AverageWatchSeconds >= 0)
             AND (VideoDurationSeconds IS NULL OR VideoDurationSeconds >= 0)
             AND (AverageWatchSeconds IS NULL OR VideoDurationSeconds IS NULL OR AverageWatchSeconds <= VideoDurationSeconds))
    );
    CREATE UNIQUE INDEX UX_CreatorStyleEvidence_Profile_Experiment
        ON dbo.CreatorStyleEvidence(CreatorStyleProfileId, StyleExperimentId);
    CREATE INDEX IX_CreatorStyleEvidence_Profile_LearnedAt
        ON dbo.CreatorStyleEvidence(CreatorStyleProfileId, LearnedAt DESC);
END;
