-- 020: scope business uniqueness to the owning workspace.
-- External creator/video/trend IDs may legitimately exist in more than one workspace.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Sql nvarchar(max);

IF OBJECT_ID(N'dbo.DouyinSourceChannels',N'U') IS NULL
 OR OBJECT_ID(N'dbo.DouyinVideoCandidates',N'U') IS NULL
 OR OBJECT_ID(N'dbo.CreatorStyleProfiles',N'U') IS NULL
 OR OBJECT_ID(N'dbo.VietnamTrendSignals',N'U') IS NULL
    THROW 52001, '020 requires the core channel, candidate, profile and trend tables.', 1;

-- Replace the old globally-unique channel key with a workspace-scoped key.
SELECT @Sql=STRING_AGG(N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.DouyinSourceChannels;',CHAR(10))
FROM sys.indexes i
WHERE i.object_id=OBJECT_ID(N'dbo.DouyinSourceChannels') AND i.is_unique=1 AND i.is_primary_key=0
  AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=1
  AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
             WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND c.name=N'DouyinSecUserId');
IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DouyinSourceChannels') AND name=N'UX_DouyinSourceChannels_Tenant_SecUserId')
    CREATE UNIQUE INDEX UX_DouyinSourceChannels_Tenant_SecUserId ON dbo.DouyinSourceChannels(TenantId,DouyinSecUserId);

-- A video may be reviewed independently by multiple workspaces.
SET @Sql=NULL;
SELECT @Sql=STRING_AGG(N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.DouyinVideoCandidates;',CHAR(10))
FROM sys.indexes i
WHERE i.object_id=OBJECT_ID(N'dbo.DouyinVideoCandidates') AND i.is_unique=1 AND i.is_primary_key=0
  AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=1
  AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
             WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND c.name=N'DouyinVideoId');
IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DouyinVideoCandidates') AND name=N'UX_DouyinVideoCandidates_Tenant_VideoId')
    CREATE UNIQUE INDEX UX_DouyinVideoCandidates_Tenant_VideoId ON dbo.DouyinVideoCandidates(TenantId,DouyinVideoId);

-- Profile keys are unique inside a workspace, not globally across all customers.
SET @Sql=NULL;
SELECT @Sql=STRING_AGG(N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.CreatorStyleProfiles;',CHAR(10))
FROM sys.indexes i
WHERE i.object_id=OBJECT_ID(N'dbo.CreatorStyleProfiles') AND i.is_unique=1 AND i.is_primary_key=0
  AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=1
  AND EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
             WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal=1 AND c.name=N'ProfileKey');
IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.CreatorStyleProfiles') AND name=N'UX_CreatorStyleProfiles_Tenant_ProfileKey')
    CREATE UNIQUE INDEX UX_CreatorStyleProfiles_Tenant_ProfileKey ON dbo.CreatorStyleProfiles(TenantId,ProfileKey);

-- Trend snapshots are stored per workspace in this schema, so deduplication is tenant-scoped.
SET @Sql=NULL;
SELECT @Sql=STRING_AGG(N'DROP INDEX '+QUOTENAME(i.name)+N' ON dbo.VietnamTrendSignals;',CHAR(10))
FROM sys.indexes i
WHERE i.object_id=OBJECT_ID(N'dbo.VietnamTrendSignals') AND i.is_unique=1 AND i.is_primary_key=0
  AND (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0)=3
  AND NOT EXISTS(
      SELECT v.ColName FROM (VALUES(N'Platform'),(N'Kind'),(N'ExternalId')) v(ColName)
      WHERE NOT EXISTS(SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                       WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0 AND c.name=v.ColName))
  AND NOT EXISTS(
      SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
      WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND ic.key_ordinal>0 AND c.name NOT IN(N'Platform',N'Kind',N'ExternalId'));
IF @Sql IS NOT NULL EXEC sys.sp_executesql @Sql;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.VietnamTrendSignals') AND name=N'UX_VietnamTrendSignals_Tenant_External')
    CREATE UNIQUE INDEX UX_VietnamTrendSignals_Tenant_External ON dbo.VietnamTrendSignals(TenantId,Platform,Kind,ExternalId);

COMMIT TRANSACTION;
