-- Trend Selection Intelligence: immutable creator decisions and downstream outcome labels.
IF OBJECT_ID(N'dbo.TrendSelectionFeedback', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.TrendSelectionFeedback
 (
  Id uniqueidentifier NOT NULL CONSTRAINT PK_TrendSelectionFeedback PRIMARY KEY,
  DouyinVideoCandidateId uniqueidentifier NOT NULL,
  Decision nvarchar(16) NOT NULL,
  ReasonCode nvarchar(60) NOT NULL,
  ReasonNote nvarchar(2000) NOT NULL CONSTRAINT DF_TrendSelectionFeedback_ReasonNote DEFAULT N'',
  CandidateSnapshotJson nvarchar(max) NOT NULL,
  FeatureSnapshotJson nvarchar(max) NOT NULL,
  ReviewedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_TrendSelectionFeedback_Candidate FOREIGN KEY (DouyinVideoCandidateId) REFERENCES dbo.DouyinVideoCandidates(Id) ON DELETE NO ACTION,
  CONSTRAINT CK_TrendSelectionFeedback_Decision CHECK (Decision IN (N'Select',N'Reject',N'Watch')),
  CONSTRAINT CK_TrendSelectionFeedback_Reason CHECK (LEN(ReasonCode) BETWEEN 1 AND 60),
  CONSTRAINT CK_TrendSelectionFeedback_CandidateJson CHECK (ISJSON(CandidateSnapshotJson)=1),
  CONSTRAINT CK_TrendSelectionFeedback_FeatureJson CHECK (ISJSON(FeatureSnapshotJson)=1)
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TrendSelectionFeedback') AND name = N'IX_TrendSelectionFeedback_Candidate_Reviewed')
    CREATE INDEX IX_TrendSelectionFeedback_Candidate_Reviewed ON dbo.TrendSelectionFeedback(DouyinVideoCandidateId,ReviewedAt DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TrendSelectionFeedback') AND name = N'IX_TrendSelectionFeedback_Decision_Reason_Reviewed')
    CREATE INDEX IX_TrendSelectionFeedback_Decision_Reason_Reviewed ON dbo.TrendSelectionFeedback(Decision,ReasonCode,ReviewedAt DESC);
IF OBJECT_ID(N'dbo.TrendCandidateOutcomeObservations', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.TrendCandidateOutcomeObservations
 (
  Id uniqueidentifier NOT NULL CONSTRAINT PK_TrendCandidateOutcomeObservations PRIMARY KEY,
  FeedbackId uniqueidentifier NOT NULL,
  OutcomeKind nvarchar(32) NOT NULL,
  OutcomeValue nvarchar(1000) NULL,
  MetricsSnapshotJson nvarchar(max) NOT NULL,
  Source nvarchar(80) NOT NULL,
  ObservedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_TrendCandidateOutcomeObservations_Feedback FOREIGN KEY (FeedbackId) REFERENCES dbo.TrendSelectionFeedback(Id) ON DELETE NO ACTION,
  CONSTRAINT CK_TrendCandidateOutcomeObservations_Kind CHECK (OutcomeKind IN (N'Adapted',N'Published',N'MarketPerformance',N'CreatorAssessment')),
  CONSTRAINT CK_TrendCandidateOutcomeObservations_MetricsJson CHECK (ISJSON(MetricsSnapshotJson)=1)
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.TrendCandidateOutcomeObservations') AND name = N'IX_TrendCandidateOutcomeObservations_Feedback_Observed')
    CREATE INDEX IX_TrendCandidateOutcomeObservations_Feedback_Observed ON dbo.TrendCandidateOutcomeObservations(FeedbackId,ObservedAt DESC);

-- Enforce append-only audit history at the database boundary, not only in the domain model.
EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_TrendSelectionFeedback_Immutable
ON dbo.TrendSelectionFeedback AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51001, ''TrendSelectionFeedback is append-only.'', 1;
END;');

EXEC(N'CREATE OR ALTER TRIGGER dbo.TR_TrendCandidateOutcomeObservations_Immutable
ON dbo.TrendCandidateOutcomeObservations AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51002, ''TrendCandidateOutcomeObservations is append-only.'', 1;
END;');
