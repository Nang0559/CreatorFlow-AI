-- 016: tenant-scoped share inbox for links explicitly shared by the creator.
-- URLs are stored only; this migration does not fetch, scrape, or authenticate to source platforms.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.SharedLinkInboxItems',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.SharedLinkInboxItems(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_SharedLinkInboxItems PRIMARY KEY,
  TenantId uniqueidentifier NOT NULL,
  Platform nvarchar(24) NOT NULL,
  SharedUrl nvarchar(1200) NOT NULL,
  UrlHash char(64) NOT NULL,
  SharedAt datetimeoffset(7) NOT NULL,
  Status nvarchar(16) NOT NULL CONSTRAINT DF_SharedLinkInboxItems_Status DEFAULT N'Pending',
  ReviewNote nvarchar(500) NULL,
  LinkedChannelId uniqueidentifier NULL,
  CONSTRAINT FK_SharedLinkInboxItems_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
  CONSTRAINT FK_SharedLinkInboxItems_Channel FOREIGN KEY(LinkedChannelId) REFERENCES dbo.DouyinSourceChannels(Id) ON DELETE NO ACTION,
  CONSTRAINT CK_SharedLinkInboxItems_Platform CHECK(Platform IN(N'Douyin',N'TikTok',N'YouTube',N'Facebook',N'Instagram')),
  CONSTRAINT CK_SharedLinkInboxItems_Status CHECK(Status IN(N'Pending',N'Reviewed',N'Linked',N'Ignored')),
  CONSTRAINT UQ_SharedLinkInboxItems_Tenant_Hash UNIQUE(TenantId,UrlHash)
 );
 CREATE INDEX IX_SharedLinkInboxItems_Tenant_Status_Time ON dbo.SharedLinkInboxItems(TenantId,Status,SharedAt DESC);
END;

COMMIT TRANSACTION;
