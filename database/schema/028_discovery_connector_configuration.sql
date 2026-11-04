IF OBJECT_ID(N'dbo.DiscoveryConnectorConfigurations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DiscoveryConnectorConfigurations
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_DiscoveryConnectorConfigurations PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        EncryptedBraveApiKey nvarchar(max) NULL,
        EncryptedTikTokResearchClientKey nvarchar(max) NULL,
        EncryptedTikTokResearchClientSecret nvarchar(max) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_DiscoveryConnectorConfigurations_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_DiscoveryConnectorConfigurations_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_DiscoveryConnectorConfigurations_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id)
    );
    CREATE UNIQUE INDEX UX_DiscoveryConnectorConfigurations_TenantId
        ON dbo.DiscoveryConnectorConfigurations(TenantId);
END;
