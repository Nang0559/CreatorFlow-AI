-- 024: tenant-scoped AI provider registry. API keys are stored only as application-encrypted ciphertext.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.Tenants',N'U') IS NULL
    THROW 52401, 'Tenant identity migration 011 is required before AI provider configuration.', 1;
IF OBJECT_ID(N'dbo.AIProviders',N'U') IS NOT NULL
    THROW 52402, 'dbo.AIProviders already exists without migration 024 being recorded. Inspect schema before continuing.', 1;

BEGIN TRANSACTION;
CREATE TABLE dbo.AIProviders
(
    Id uniqueidentifier NOT NULL CONSTRAINT DF_AIProviders_Id DEFAULT NEWSEQUENTIALID(),
    TenantId uniqueidentifier NOT NULL,
    DisplayName nvarchar(120) NOT NULL,
    ProviderType nvarchar(32) NOT NULL,
    BaseUrl nvarchar(500) NOT NULL,
    Model nvarchar(160) NOT NULL,
    EncryptedApiKey nvarchar(max) NULL,
    IsActive bit NOT NULL CONSTRAINT DF_AIProviders_IsActive DEFAULT (1),
    RoutingRole nvarchar(16) NOT NULL,
    FallbackOrder int NULL,
    TimeoutSeconds int NOT NULL CONSTRAINT DF_AIProviders_Timeout DEFAULT (120),
    MaxOutputTokens int NOT NULL CONSTRAINT DF_AIProviders_MaxTokens DEFAULT (3000),
    CreatedAt datetimeoffset(7) NOT NULL CONSTRAINT DF_AIProviders_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetimeoffset(7) NOT NULL CONSTRAINT DF_AIProviders_UpdatedAt DEFAULT SYSUTCDATETIME(),
    RowVersion rowversion NOT NULL,
    CONSTRAINT PK_AIProviders PRIMARY KEY (Id),
    CONSTRAINT FK_AIProviders_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
    CONSTRAINT CK_AIProviders_Type CHECK (ProviderType IN (N'openai-compatible')),
    CONSTRAINT CK_AIProviders_Role CHECK (RoutingRole IN (N'Primary', N'Fallback')),
    CONSTRAINT CK_AIProviders_FallbackOrder CHECK
        ((RoutingRole = N'Primary' AND FallbackOrder IS NULL) OR
         (RoutingRole = N'Fallback' AND FallbackOrder BETWEEN 1 AND 20)),
    CONSTRAINT CK_AIProviders_Timeout CHECK (TimeoutSeconds BETWEEN 5 AND 300),
    CONSTRAINT CK_AIProviders_MaxTokens CHECK (MaxOutputTokens BETWEEN 256 AND 16000),
    CONSTRAINT CK_AIProviders_DisplayName CHECK (LEN(LTRIM(RTRIM(DisplayName))) > 0),
    CONSTRAINT CK_AIProviders_Model CHECK (LEN(LTRIM(RTRIM(Model))) > 0),
    CONSTRAINT CK_AIProviders_BaseUrl CHECK (LEN(LTRIM(RTRIM(BaseUrl))) > 0)
);
CREATE UNIQUE INDEX UX_AIProviders_Tenant_Primary
    ON dbo.AIProviders(TenantId) WHERE RoutingRole = N'Primary' AND IsActive = 1;
CREATE UNIQUE INDEX UX_AIProviders_Tenant_FallbackOrder
    ON dbo.AIProviders(TenantId, FallbackOrder)
    WHERE RoutingRole = N'Fallback' AND IsActive = 1;
CREATE INDEX IX_AIProviders_Tenant_Active
    ON dbo.AIProviders(TenantId, IsActive, RoutingRole, FallbackOrder);
COMMIT TRANSACTION;
