-- 022: tenant subscription entitlements and auditable resource usage ledger.
-- This migration establishes the SaaS resource model; payment-provider checkout and
-- automatic metering integrations are separate capabilities and are not implied here.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.Tenants',N'U') IS NULL
    THROW 52201, 'Migration 011 tenant foundation is required.', 1;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.CreatorFlowPlans',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.CreatorFlowPlans(
  PlanCode nvarchar(40) NOT NULL CONSTRAINT PK_CreatorFlowPlans PRIMARY KEY,
  DisplayName nvarchar(120) NOT NULL,
  Description nvarchar(1000) NOT NULL CONSTRAINT DF_CreatorFlowPlans_Description DEFAULT N'',
  CurrencyCode char(3) NOT NULL CONSTRAINT DF_CreatorFlowPlans_Currency DEFAULT 'VND',
  MonthlyPriceMinor bigint NULL,
  MaxMembers int NOT NULL,
  MaxProjects int NOT NULL,
  MaxStorageBytes bigint NOT NULL,
  MonthlyAiCredits bigint NOT NULL,
  MonthlyRenderSeconds bigint NOT NULL,
  MaxConcurrentRenders int NOT NULL,
  IsAvailable bit NOT NULL CONSTRAINT DF_CreatorFlowPlans_Available DEFAULT 0,
  CreatedAt datetimeoffset(7) NOT NULL,
  UpdatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT CK_CreatorFlowPlans_Price CHECK(MonthlyPriceMinor IS NULL OR MonthlyPriceMinor>=0),
  CONSTRAINT CK_CreatorFlowPlans_Limits CHECK(MaxMembers>=1 AND MaxProjects>=1 AND MaxStorageBytes>=0 AND MonthlyAiCredits>=0 AND MonthlyRenderSeconds>=0 AND MaxConcurrentRenders>=1)
 );
END;

IF OBJECT_ID(N'dbo.TenantSubscriptions',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.TenantSubscriptions(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_TenantSubscriptions PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  PlanCode nvarchar(40) NOT NULL,
  Status nvarchar(20) NOT NULL,
  BillingProvider nvarchar(40) NULL,
  ExternalCustomerId nvarchar(200) NULL,
  ExternalSubscriptionId nvarchar(200) NULL,
  CurrentPeriodStart datetimeoffset(7) NOT NULL,
  CurrentPeriodEnd datetimeoffset(7) NOT NULL,
  TrialEndsAt datetimeoffset(7) NULL,
  CancelAtPeriodEnd bit NOT NULL CONSTRAINT DF_TenantSubscriptions_Cancel DEFAULT 0,
  CreatedAt datetimeoffset(7) NOT NULL,
  UpdatedAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_TenantSubscriptions_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_TenantSubscriptions_Plan FOREIGN KEY(PlanCode) REFERENCES dbo.CreatorFlowPlans(PlanCode) ON DELETE NO ACTION,
  CONSTRAINT CK_TenantSubscriptions_Status CHECK(Status IN(N'Trialing',N'Active',N'PastDue',N'Suspended',N'Cancelled')),
  CONSTRAINT CK_TenantSubscriptions_Period CHECK(CurrentPeriodEnd>CurrentPeriodStart)
 );
 CREATE INDEX IX_TenantSubscriptions_Tenant_Status ON dbo.TenantSubscriptions(TenantId,Status,CurrentPeriodEnd DESC);
 CREATE UNIQUE INDEX UX_TenantSubscriptions_External ON dbo.TenantSubscriptions(BillingProvider,ExternalSubscriptionId)
  WHERE BillingProvider IS NOT NULL AND ExternalSubscriptionId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.TenantUsageEvents',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.TenantUsageEvents(
  Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantUsageEvents PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  MetricCode nvarchar(40) NOT NULL,
  Quantity decimal(20,4) NOT NULL,
  UnitCode nvarchar(20) NOT NULL,
  IdempotencyKey nvarchar(160) NOT NULL,
  ResourceReference nvarchar(200) NULL,
  MetadataJson nvarchar(2000) NOT NULL CONSTRAINT DF_TenantUsageEvents_Metadata DEFAULT N'{}',
  OccurredAt datetimeoffset(7) NOT NULL,
  CONSTRAINT FK_TenantUsageEvents_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
  CONSTRAINT CK_TenantUsageEvents_Quantity CHECK(Quantity>0),
  CONSTRAINT UQ_TenantUsageEvents_Tenant_Metric_Idempotency UNIQUE(TenantId,MetricCode,IdempotencyKey)
 );
 CREATE INDEX IX_TenantUsageEvents_Tenant_Metric_Date ON dbo.TenantUsageEvents(TenantId,MetricCode,OccurredAt)
  INCLUDE(Quantity,UnitCode);
END;

IF OBJECT_ID(N'dbo.TenantStorageObjects',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.TenantStorageObjects(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_TenantStorageObjects PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  StorageKey nvarchar(1000) NOT NULL,
  OriginalFileName nvarchar(260) NOT NULL,
  ContentType nvarchar(160) NOT NULL,
  SizeBytes bigint NOT NULL,
  Sha256 char(64) NOT NULL,
  State nvarchar(16) NOT NULL,
  CreatedAt datetimeoffset(7) NOT NULL,
  DeletedAt datetimeoffset(7) NULL,
  CONSTRAINT FK_TenantStorageObjects_Tenant FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
  CONSTRAINT UQ_TenantStorageObjects_Tenant_Key UNIQUE(TenantId,StorageKey),
  CONSTRAINT CK_TenantStorageObjects_Size CHECK(SizeBytes>0),
  CONSTRAINT CK_TenantStorageObjects_Hash CHECK(Sha256 NOT LIKE '%[^0-9a-fA-F]%'),
  CONSTRAINT CK_TenantStorageObjects_State CHECK(State IN(N'Active',N'Deleted',N'Quarantined'))
 );
 CREATE INDEX IX_TenantStorageObjects_Tenant_State ON dbo.TenantStorageObjects(TenantId,State,CreatedAt DESC)
  INCLUDE(SizeBytes);
END;

COMMIT TRANSACTION;
