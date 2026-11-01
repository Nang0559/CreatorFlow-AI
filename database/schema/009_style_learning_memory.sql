-- Persist reviewable AI learning proposals and immutable approved creator-style versions.
IF OBJECT_ID(N'dbo.StyleLearningProposals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StyleLearningProposals
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_StyleLearningProposals PRIMARY KEY,
        CreatorStyleProfileId uniqueidentifier NOT NULL,
        ProposedSummary nvarchar(4000) NOT NULL,
        EvidenceExperimentIdsJson nvarchar(max) NOT NULL,
        EvidenceSnapshotJson nvarchar(max) NOT NULL,
        AlgorithmVersion nvarchar(80) NOT NULL,
        Status nvarchar(16) NOT NULL,
        ReviewReason nvarchar(2000) NULL,
        ReviewerId nvarchar(160) NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        ReviewedAt datetimeoffset(7) NULL,
        CONSTRAINT FK_StyleLearningProposals_Profiles FOREIGN KEY (CreatorStyleProfileId)
            REFERENCES dbo.CreatorStyleProfiles(Id) ON DELETE CASCADE,
        CONSTRAINT CK_StyleLearningProposals_Status CHECK (Status IN (N'Pending', N'Approved', N'Rejected')),
        CONSTRAINT CK_StyleLearningProposals_Review CHECK
            ((Status = N'Pending' AND ReviewedAt IS NULL AND ReviewerId IS NULL)
             OR (Status IN (N'Approved', N'Rejected') AND ReviewedAt IS NOT NULL AND ReviewerId IS NOT NULL))
    );
    CREATE INDEX IX_StyleLearningProposals_Profile_Status_Created
        ON dbo.StyleLearningProposals(CreatorStyleProfileId, Status, CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.CreatorStyleProfileVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CreatorStyleProfileVersions
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_CreatorStyleProfileVersions PRIMARY KEY,
        CreatorStyleProfileId uniqueidentifier NOT NULL,
        ApprovedProposalId uniqueidentifier NULL,
        VersionNumber int NOT NULL,
        DisplayName nvarchar(160) NOT NULL,
        Positioning nvarchar(1000) NOT NULL,
        StyleSummary nvarchar(4000) NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        CreatedBy nvarchar(160) NOT NULL,
        CONSTRAINT FK_CreatorStyleProfileVersions_Profiles FOREIGN KEY (CreatorStyleProfileId)
            REFERENCES dbo.CreatorStyleProfiles(Id) ON DELETE CASCADE,
        CONSTRAINT FK_CreatorStyleProfileVersions_Proposals FOREIGN KEY (ApprovedProposalId)
            REFERENCES dbo.StyleLearningProposals(Id) ON DELETE NO ACTION,
        CONSTRAINT UQ_CreatorStyleProfileVersions_Profile_Version UNIQUE (CreatorStyleProfileId, VersionNumber),
        CONSTRAINT UQ_CreatorStyleProfileVersions_Proposal UNIQUE (ApprovedProposalId),
        CONSTRAINT CK_CreatorStyleProfileVersions_Version CHECK (VersionNumber > 0)
    );
END;
