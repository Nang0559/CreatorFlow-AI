IF COL_LENGTH(N'dbo.DiscoveryConnectorConfigurations', N'EncryptedCustomProvidersJson') IS NULL
    ALTER TABLE dbo.DiscoveryConnectorConfigurations ADD EncryptedCustomProvidersJson nvarchar(max) NULL;
