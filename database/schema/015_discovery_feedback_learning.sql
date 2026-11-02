-- 015: explainable feedback learning memory for discovery.
-- Feedback is evidence; one rejection never becomes a permanent global rule.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DiscoveryFeedbackEvents',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryFeedbackEvents(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryFeedbackEvents PRIMARY KEY,
  CandidateId uniqueidentifier NOT NULL,
  Decision nvarchar(20) NOT NULL,
  ReasonCode nvarchar(60) NOT NULL,
  ReasonText nvarchar(1000) NULL,
  ContextJson nvarchar(max) NOT NULL CONSTRAINT DF_DiscoveryFeedbackEvents_Context DEFAULT N'{}',
  CreatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryFeedbackEvents_Candidate FOREIGN KEY(CandidateId) REFERENCES dbo.DiscoveryCandidates(Id) ON DELETE NO ACTION,
  CONSTRAINT CK_DiscoveryFeedbackEvents_Decision CHECK(Decision IN(N'Interested',N'NotRelevant',N'Save',N'Hide',N'Followed',N'Converted')),
  CONSTRAINT CK_DiscoveryFeedbackEvents_Context CHECK(ISJSON(ContextJson)=1)
 );
 CREATE INDEX IX_DiscoveryFeedbackEvents_Reason_Time ON dbo.DiscoveryFeedbackEvents(ReasonCode,CreatedAt DESC);
 CREATE INDEX IX_DiscoveryFeedbackEvents_Candidate_Time ON dbo.DiscoveryFeedbackEvents(CandidateId,CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryCandidateFeatureSnapshots',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryCandidateFeatureSnapshots(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryCandidateFeatureSnapshots PRIMARY KEY,
  CandidateId uniqueidentifier NOT NULL,
  FeatureVersion nvarchar(80) NOT NULL,
  FeaturesJson nvarchar(max) NOT NULL,
  CapturedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryCandidateFeatureSnapshots_Candidate FOREIGN KEY(CandidateId) REFERENCES dbo.DiscoveryCandidates(Id) ON DELETE CASCADE,
  CONSTRAINT CK_DiscoveryCandidateFeatureSnapshots_Json CHECK(ISJSON(FeaturesJson)=1)
 );
 CREATE INDEX IX_DiscoveryCandidateFeatureSnapshots_Candidate_Time ON dbo.DiscoveryCandidateFeatureSnapshots(CandidateId,CapturedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryLearningInsights',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryLearningInsights(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryLearningInsights PRIMARY KEY,
  FeedbackEventId uniqueidentifier NOT NULL,
  InsightVersion nvarchar(80) NOT NULL,
  ConfirmedSignalsJson nvarchar(max) NOT NULL,
  UncertainSignalsJson nvarchar(max) NOT NULL,
  ProposedRuleJson nvarchar(max) NOT NULL,
  Status nvarchar(20) NOT NULL CONSTRAINT DF_DiscoveryLearningInsights_Status DEFAULT N'Pending',
  ReviewedAt datetimeoffset(7) NULL,
  CreatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryLearningInsights_Feedback FOREIGN KEY(FeedbackEventId) REFERENCES dbo.DiscoveryFeedbackEvents(Id) ON DELETE CASCADE,
  CONSTRAINT CK_DiscoveryLearningInsights_Status CHECK(Status IN(N'Pending',N'Accepted',N'Rejected',N'Superseded')),
  CONSTRAINT CK_DiscoveryLearningInsights_Json CHECK(ISJSON(ConfirmedSignalsJson)=1 AND ISJSON(UncertainSignalsJson)=1 AND ISJSON(ProposedRuleJson)=1)
 );
 CREATE INDEX IX_DiscoveryLearningInsights_Status_Time ON dbo.DiscoveryLearningInsights(Status,CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryPreferenceRules',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryPreferenceRules(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryPreferenceRules PRIMARY KEY,
  ProfileKey nvarchar(200) NOT NULL,
  Scope nvarchar(20) NOT NULL,
  FeatureKey nvarchar(100) NOT NULL,
  Operator nvarchar(20) NOT NULL,
  FeatureValue nvarchar(300) NOT NULL,
  Effect nvarchar(10) NOT NULL,
  Weight decimal(7,3) NOT NULL,
  EvidenceCount int NOT NULL,
  Confidence decimal(5,4) NOT NULL,
  SourceInsightId uniqueidentifier NULL,
  IsActive bit NOT NULL CONSTRAINT DF_DiscoveryPreferenceRules_Active DEFAULT 0,
  ValidFrom datetimeoffset(7) NOT NULL,
  ValidUntil datetimeoffset(7) NULL,
  UpdatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryPreferenceRules_Insight FOREIGN KEY(SourceInsightId) REFERENCES dbo.DiscoveryLearningInsights(Id) ON DELETE SET NULL,
  CONSTRAINT CK_DiscoveryPreferenceRules_Scope CHECK(Scope IN(N'Topic',N'Platform',N'Channel',N'Global')),
  CONSTRAINT CK_DiscoveryPreferenceRules_Operator CHECK(Operator IN(N'Equals',N'Contains',N'AtLeast',N'AtMost')),
  CONSTRAINT CK_DiscoveryPreferenceRules_Effect CHECK(Effect IN(N'Boost',N'Penalty')),
  CONSTRAINT CK_DiscoveryPreferenceRules_Weight CHECK(Weight BETWEEN 0 AND 30 AND EvidenceCount>=1 AND Confidence BETWEEN 0 AND 1),
  CONSTRAINT CK_DiscoveryPreferenceRules_Dates CHECK(ValidUntil IS NULL OR ValidUntil>ValidFrom)
 );
 CREATE INDEX IX_DiscoveryPreferenceRules_Profile_Active ON dbo.DiscoveryPreferenceRules(ProfileKey,IsActive,ValidUntil);
END;

IF OBJECT_ID(N'dbo.DiscoveryLearningModelVersions',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryLearningModelVersions(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryLearningModelVersions PRIMARY KEY,
  ModelVersion nvarchar(80) NOT NULL,
  FeatureVersion nvarchar(80) NOT NULL,
  DatasetVersion nvarchar(80) NOT NULL,
  TrainingRows int NOT NULL,
  ValidationRows int NOT NULL,
  PrecisionAtK decimal(7,5) NULL,
  RecallAtK decimal(7,5) NULL,
  NdcgAtK decimal(7,5) NULL,
  CalibrationError decimal(7,5) NULL,
  IsActive bit NOT NULL CONSTRAINT DF_DiscoveryLearningModelVersions_Active DEFAULT 0,
  CreatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT UQ_DiscoveryLearningModelVersions_Version UNIQUE(ModelVersion),
  CONSTRAINT CK_DiscoveryLearningModelVersions_Counts CHECK(TrainingRows>=0 AND ValidationRows>=0)
 );
END;

COMMIT TRANSACTION;
