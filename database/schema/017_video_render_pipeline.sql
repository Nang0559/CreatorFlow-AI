-- 017: tenant-scoped media assets and durable video render jobs.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.Tenants',N'U') IS NULL THROW 51701, '017 requires dbo.Tenants.', 1;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.VideoAssets',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.VideoAssets(Id uniqueidentifier NOT NULL CONSTRAINT PK_VideoAssets PRIMARY KEY,TenantId uniqueidentifier NOT NULL,OriginalFileName nvarchar(260) NOT NULL,StoredFileName nvarchar(120) NOT NULL,StoragePath nvarchar(1000) NOT NULL,ContentType nvarchar(100) NOT NULL,FileSizeBytes bigint NOT NULL,Sha256 char(64) NOT NULL,RightsNote nvarchar(1000) NULL,CreatedAt datetimeoffset(7) NOT NULL,CONSTRAINT FK_VideoAssets_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,CONSTRAINT CK_VideoAssets_Size CHECK(FileSizeBytes>0 AND FileSizeBytes<=524288000));
 CREATE UNIQUE INDEX UX_VideoAssets_Tenant_Sha256 ON dbo.VideoAssets(TenantId,Sha256);
 CREATE INDEX IX_VideoAssets_TenantId ON dbo.VideoAssets(TenantId,CreatedAt DESC);
END;
IF OBJECT_ID(N'dbo.VideoRenderJobs',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.VideoRenderJobs(Id uniqueidentifier NOT NULL CONSTRAINT PK_VideoRenderJobs PRIMARY KEY,TenantId uniqueidentifier NOT NULL,AssetId uniqueidentifier NOT NULL,StyleKey nvarchar(80) NOT NULL,Title nvarchar(180) NOT NULL,Hook nvarchar(500) NOT NULL,CallToAction nvarchar(300) NULL,Status nvarchar(24) NOT NULL CONSTRAINT DF_VideoRenderJobs_Status DEFAULT N'Queued',ProgressPercent tinyint NOT NULL CONSTRAINT DF_VideoRenderJobs_Progress DEFAULT 0,SourcePath nvarchar(1000) NOT NULL,OutputPath nvarchar(1000) NOT NULL,ErrorMessage nvarchar(2000) NULL,AttemptCount tinyint NOT NULL CONSTRAINT DF_VideoRenderJobs_Attempts DEFAULT 0,CreatedAt datetimeoffset(7) NOT NULL,StartedAt datetimeoffset(7) NULL,CompletedAt datetimeoffset(7) NULL,DurationSeconds decimal(12,3) NULL,Width int NULL,Height int NULL,Sha256 char(64) NULL,CONSTRAINT FK_VideoRenderJobs_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,CONSTRAINT FK_VideoRenderJobs_Assets FOREIGN KEY(AssetId) REFERENCES dbo.VideoAssets(Id) ON DELETE NO ACTION,CONSTRAINT CK_VideoRenderJobs_Status CHECK(Status IN(N'Queued',N'Rendering',N'Encoding',N'Completed',N'Failed',N'Cancelled')),CONSTRAINT CK_VideoRenderJobs_Progress CHECK(ProgressPercent BETWEEN 0 AND 100),CONSTRAINT CK_VideoRenderJobs_Attempts CHECK(AttemptCount BETWEEN 0 AND 3));
 CREATE INDEX IX_VideoRenderJobs_Queue ON dbo.VideoRenderJobs(Status,CreatedAt) INCLUDE(AttemptCount);
 CREATE INDEX IX_VideoRenderJobs_TenantId ON dbo.VideoRenderJobs(TenantId,CreatedAt DESC);
 CREATE INDEX IX_VideoRenderJobs_Asset ON dbo.VideoRenderJobs(TenantId,AssetId);
END;
COMMIT TRANSACTION;