-- 018: enforce same-tenant relationships for media and shared channel links.
-- The application also scopes every read/write by TenantId; these composite FKs
-- make cross-tenant references impossible even for direct SQL writes.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.VideoAssets',N'U') IS NULL
    THROW 51801, '018 requires dbo.VideoAssets.', 1;
IF OBJECT_ID(N'dbo.VideoRenderJobs',N'U') IS NULL
    THROW 51802, '018 requires dbo.VideoRenderJobs.', 1;
IF OBJECT_ID(N'dbo.DouyinSourceChannels',N'U') IS NULL
    THROW 51803, '018 requires dbo.DouyinSourceChannels.', 1;
IF OBJECT_ID(N'dbo.SharedLinkInboxItems',N'U') IS NULL
    THROW 51804, '018 requires dbo.SharedLinkInboxItems.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.VideoAssets') AND name=N'UX_VideoAssets_Tenant_Id')
    CREATE UNIQUE INDEX UX_VideoAssets_Tenant_Id ON dbo.VideoAssets(TenantId,Id);

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.VideoRenderJobs') AND name=N'FK_VideoRenderJobs_Assets')
    ALTER TABLE dbo.VideoRenderJobs DROP CONSTRAINT FK_VideoRenderJobs_Assets;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.VideoRenderJobs') AND name=N'FK_VideoRenderJobs_Tenant_Asset')
    ALTER TABLE dbo.VideoRenderJobs WITH CHECK ADD CONSTRAINT FK_VideoRenderJobs_Tenant_Asset
        FOREIGN KEY(TenantId,AssetId) REFERENCES dbo.VideoAssets(TenantId,Id) ON DELETE NO ACTION;
ALTER TABLE dbo.VideoRenderJobs CHECK CONSTRAINT FK_VideoRenderJobs_Tenant_Asset;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DouyinSourceChannels') AND name=N'UX_DouyinSourceChannels_Tenant_Id')
    CREATE UNIQUE INDEX UX_DouyinSourceChannels_Tenant_Id ON dbo.DouyinSourceChannels(TenantId,Id);

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.SharedLinkInboxItems') AND name=N'FK_SharedLinkInboxItems_Channel')
    ALTER TABLE dbo.SharedLinkInboxItems DROP CONSTRAINT FK_SharedLinkInboxItems_Channel;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.SharedLinkInboxItems') AND name=N'FK_SharedLinkInboxItems_Tenant_Channel')
    ALTER TABLE dbo.SharedLinkInboxItems WITH CHECK ADD CONSTRAINT FK_SharedLinkInboxItems_Tenant_Channel
        FOREIGN KEY(TenantId,LinkedChannelId) REFERENCES dbo.DouyinSourceChannels(TenantId,Id) ON DELETE NO ACTION;
ALTER TABLE dbo.SharedLinkInboxItems CHECK CONSTRAINT FK_SharedLinkInboxItems_Tenant_Channel;

COMMIT TRANSACTION;
