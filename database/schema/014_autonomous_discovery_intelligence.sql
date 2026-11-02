-- 014: autonomous topic discovery, optional priority links, evidence snapshots and ranking.
-- No platform login/session is used. Sources must be public, official or explicitly permitted.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.DiscoveryRuns', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryRuns(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryRuns PRIMARY KEY,
  RunKey nvarchar(120) NOT NULL,
  Status nvarchar(20) NOT NULL,
  StartedAt datetimeoffset(7) NOT NULL,
  CompletedAt datetimeoffset(7) NULL,
  QueryPlanJson nvarchar(max) NOT NULL,
  CoverageJson nvarchar(max) NOT NULL CONSTRAINT DF_DiscoveryRuns_Coverage DEFAULT N'{}',
  ErrorCode nvarchar(80) NULL,
  CONSTRAINT UQ_DiscoveryRuns_RunKey UNIQUE(RunKey),
  CONSTRAINT CK_DiscoveryRuns_Status CHECK(Status IN(N'Queued',N'Running',N'Completed',N'Partial',N'Failed')),
  CONSTRAINT CK_DiscoveryRuns_QueryPlan CHECK(ISJSON(QueryPlanJson)=1),
  CONSTRAINT CK_DiscoveryRuns_Coverage CHECK(ISJSON(CoverageJson)=1)
 );
 CREATE INDEX IX_DiscoveryRuns_Status_Started ON dbo.DiscoveryRuns(Status,StartedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryTopicProfiles', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryTopicProfiles(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryTopicProfiles PRIMARY KEY,
  ContentTopicId uniqueidentifier NOT NULL,
  TargetMarket nvarchar(40) NOT NULL CONSTRAINT DF_DiscoveryTopicProfiles_Market DEFAULT N'Vietnam',
  ProductContext nvarchar(1000) NOT NULL CONSTRAINT DF_DiscoveryTopicProfiles_Product DEFAULT N'',
  AudienceContext nvarchar(1000) NOT NULL CONSTRAINT DF_DiscoveryTopicProfiles_Audience DEFAULT N'',
  IsEnabled bit NOT NULL CONSTRAINT DF_DiscoveryTopicProfiles_Enabled DEFAULT 1,
  RefreshMinutes int NOT NULL CONSTRAINT DF_DiscoveryTopicProfiles_Refresh DEFAULT 30,
  CreatedAt datetimeoffset(7) NOT NULL,
  UpdatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryTopicProfiles_Topics FOREIGN KEY(ContentTopicId) REFERENCES dbo.ContentTopics(Id) ON DELETE CASCADE,
  CONSTRAINT UQ_DiscoveryTopicProfiles_Topic UNIQUE(ContentTopicId),
  CONSTRAINT CK_DiscoveryTopicProfiles_Refresh CHECK(RefreshMinutes BETWEEN 5 AND 1440)
 );
END;

IF OBJECT_ID(N'dbo.DiscoverySources', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoverySources(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoverySources PRIMARY KEY,
  Platform nvarchar(40) NOT NULL,
  Provider nvarchar(120) NOT NULL,
  SourceKind nvarchar(24) NOT NULL,
  BaseUrl nvarchar(1000) NOT NULL,
  AccessMode nvarchar(24) NOT NULL,
  IsEnabled bit NOT NULL CONSTRAINT DF_DiscoverySources_Enabled DEFAULT 0,
  TermsReviewedAt datetimeoffset(7) NULL,
  LastSuccessAt datetimeoffset(7) NULL,
  CreatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT CK_DiscoverySources_Kind CHECK(SourceKind IN(N'OfficialApi',N'PermittedProvider',N'PublicFeed',N'ManualImport')),
  CONSTRAINT CK_DiscoverySources_Access CHECK(AccessMode IN(N'PublicRead',N'AuthorizedRead',N'Manual')),
  CONSTRAINT CK_DiscoverySources_NoSession CHECK(AccessMode <> N'AuthorizedRead' OR Platform NOT IN(N'Douyin')),
  CONSTRAINT UQ_DiscoverySources_Provider UNIQUE(Platform,Provider)
 );
END;

IF OBJECT_ID(N'dbo.DiscoveryCandidates', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryCandidates(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryCandidates PRIMARY KEY,
  Platform nvarchar(40) NOT NULL,
  CandidateKind nvarchar(20) NOT NULL,
  ExternalId nvarchar(180) NOT NULL,
  SourceChannelExternalId nvarchar(180) NULL,
  Title nvarchar(500) NOT NULL CONSTRAINT DF_DiscoveryCandidates_Title DEFAULT N'',
  CreatorName nvarchar(200) NOT NULL CONSTRAINT DF_DiscoveryCandidates_Creator DEFAULT N'',
  CandidateUrl nvarchar(1200) NOT NULL,
  TopicText nvarchar(300) NULL,
  DescriptionText nvarchar(2000) NULL,
  SourceId uniqueidentifier NULL,
  FirstSeenAt datetimeoffset(7) NOT NULL,
  LastSeenAt datetimeoffset(7) NOT NULL,
  IsEligible bit NOT NULL CONSTRAINT DF_DiscoveryCandidates_Eligible DEFAULT 1,
  CONSTRAINT CK_DiscoveryCandidates_Kind CHECK(CandidateKind IN(N'Video',N'Channel',N'Sound')),
  CONSTRAINT FK_DiscoveryCandidates_Source FOREIGN KEY(SourceId) REFERENCES dbo.DiscoverySources(Id) ON DELETE SET NULL,
  CONSTRAINT UQ_DiscoveryCandidates_Platform_Kind_External UNIQUE(Platform,CandidateKind,ExternalId)
 );
 CREATE INDEX IX_DiscoveryCandidates_Kind_LastSeen ON dbo.DiscoveryCandidates(CandidateKind,LastSeenAt DESC) INCLUDE(Platform,IsEligible);
END;

IF OBJECT_ID(N'dbo.DiscoveryMetricSnapshots', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryMetricSnapshots(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryMetricSnapshots PRIMARY KEY,
  CandidateId uniqueidentifier NOT NULL,
  RunId uniqueidentifier NULL,
  Views bigint NULL, Likes bigint NULL, Comments bigint NULL, Shares bigint NULL, Saves bigint NULL,
  PublishedAt datetimeoffset(7) NULL,
  ObservedAt datetimeoffset(7) NOT NULL,
  Coverage decimal(5,4) NOT NULL,
  RawMetricJson nvarchar(max) NOT NULL CONSTRAINT DF_DiscoveryMetricSnapshots_Raw DEFAULT N'{}',
  CONSTRAINT FK_DiscoveryMetricSnapshots_Candidate FOREIGN KEY(CandidateId) REFERENCES dbo.DiscoveryCandidates(Id) ON DELETE CASCADE,
  CONSTRAINT FK_DiscoveryMetricSnapshots_Run FOREIGN KEY(RunId) REFERENCES dbo.DiscoveryRuns(Id) ON DELETE SET NULL,
  CONSTRAINT CK_DiscoveryMetricSnapshots_NonNegative CHECK((Views IS NULL OR Views>=0) AND (Likes IS NULL OR Likes>=0) AND (Comments IS NULL OR Comments>=0) AND (Shares IS NULL OR Shares>=0) AND (Saves IS NULL OR Saves>=0)),
  CONSTRAINT CK_DiscoveryMetricSnapshots_Coverage CHECK(Coverage BETWEEN 0 AND 1),
  CONSTRAINT CK_DiscoveryMetricSnapshots_Json CHECK(ISJSON(RawMetricJson)=1)
 );
 CREATE INDEX IX_DiscoveryMetricSnapshots_Candidate_Time ON dbo.DiscoveryMetricSnapshots(CandidateId,ObservedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryRankings', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryRankings(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryRankings PRIMARY KEY,
  CandidateId uniqueidentifier NOT NULL,
  RunId uniqueidentifier NULL,
  AlgorithmVersion nvarchar(80) NOT NULL,
  Score decimal(9,4) NOT NULL,
  TopicFit decimal(9,4) NOT NULL,
  Momentum decimal(9,4) NOT NULL,
  MarketFit decimal(9,4) NOT NULL,
  CreatorQuality decimal(9,4) NOT NULL,
  PriorityBoost decimal(9,4) NOT NULL CONSTRAINT DF_DiscoveryRankings_Priority DEFAULT 0,
  Confidence decimal(5,4) NOT NULL,
  ExplanationJson nvarchar(max) NOT NULL,
  RankedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryRankings_Candidate FOREIGN KEY(CandidateId) REFERENCES dbo.DiscoveryCandidates(Id) ON DELETE CASCADE,
  CONSTRAINT FK_DiscoveryRankings_Run FOREIGN KEY(RunId) REFERENCES dbo.DiscoveryRuns(Id) ON DELETE SET NULL,
  CONSTRAINT CK_DiscoveryRankings_Score CHECK(Score BETWEEN 0 AND 100 AND TopicFit BETWEEN 0 AND 100 AND Momentum BETWEEN 0 AND 100 AND MarketFit BETWEEN 0 AND 100 AND CreatorQuality BETWEEN 0 AND 100 AND PriorityBoost BETWEEN 0 AND 30 AND Confidence BETWEEN 0 AND 1),
  CONSTRAINT CK_DiscoveryRankings_Explanation CHECK(ISJSON(ExplanationJson)=1)
 );
 CREATE INDEX IX_DiscoveryRankings_Score ON dbo.DiscoveryRankings(Score DESC,RankedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryPriorityLinks', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryPriorityLinks(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryPriorityLinks PRIMARY KEY,
  CanonicalUrl nvarchar(1200) NOT NULL,
  Platform nvarchar(40) NOT NULL,
  CandidateId uniqueidentifier NULL,
  SubmittedAt datetimeoffset(7) NOT NULL,
  PriorityWeight decimal(5,2) NOT NULL CONSTRAINT DF_DiscoveryPriorityLinks_Weight DEFAULT 20,
  Note nvarchar(500) NULL,
  CONSTRAINT FK_DiscoveryPriorityLinks_Candidate FOREIGN KEY(CandidateId) REFERENCES dbo.DiscoveryCandidates(Id) ON DELETE SET NULL,
  CONSTRAINT UQ_DiscoveryPriorityLinks_Url UNIQUE(CanonicalUrl),
  CONSTRAINT CK_DiscoveryPriorityLinks_Weight CHECK(PriorityWeight BETWEEN 0 AND 30)
 );
 CREATE INDEX IX_DiscoveryPriorityLinks_Submitted ON dbo.DiscoveryPriorityLinks(SubmittedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryFeedback', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryFeedback(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryFeedback PRIMARY KEY,
  CandidateId uniqueidentifier NOT NULL,
  Decision nvarchar(16) NOT NULL,
  ReasonCode nvarchar(60) NOT NULL,
  CreatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_DiscoveryFeedback_Candidate FOREIGN KEY(CandidateId) REFERENCES dbo.DiscoveryCandidates(Id) ON DELETE NO ACTION,
  CONSTRAINT CK_DiscoveryFeedback_Decision CHECK(Decision IN(N'Interested',N'NotRelevant',N'Save',N'Hide'))
 );
 CREATE INDEX IX_DiscoveryFeedback_Candidate_Time ON dbo.DiscoveryFeedback(CandidateId,CreatedAt DESC);
END;

IF OBJECT_ID(N'dbo.DiscoveryModelEvaluations', N'U') IS NULL
BEGIN
 CREATE TABLE dbo.DiscoveryModelEvaluations(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryModelEvaluations PRIMARY KEY,
  ModelVersion nvarchar(80) NOT NULL,
  DatasetVersion nvarchar(80) NOT NULL,
  TrainCount int NOT NULL,
  ValidationCount int NOT NULL,
  PrecisionAtK decimal(7,5) NULL,
  RecallAtK decimal(7,5) NULL,
  NdcgAtK decimal(7,5) NULL,
  EvaluatedAt datetimeoffset(7) NOT NULL,
  Notes nvarchar(2000) NULL,
  CONSTRAINT CK_DiscoveryModelEvaluations_Counts CHECK(TrainCount>=0 AND ValidationCount>=0)
 );
END;

COMMIT TRANSACTION;
