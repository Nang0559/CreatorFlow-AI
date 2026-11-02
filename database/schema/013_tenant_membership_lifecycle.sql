-- Tenant membership lifecycle: single-use invitations and auditable security events.
-- No passwords or raw invitation tokens are stored in SQL Server.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.TenantInvitations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TenantInvitations
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_TenantInvitations PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        Email nvarchar(320) NOT NULL,
        Role nvarchar(16) NOT NULL,
        TokenHash char(64) NOT NULL,
        ExpiresAt datetimeoffset(7) NOT NULL,
        CreatedByIssuer nvarchar(500) NOT NULL,
        CreatedBySubject nvarchar(200) NOT NULL,
        CreatedAt datetimeoffset(7) NOT NULL,
        Status nvarchar(16) NOT NULL CONSTRAINT DF_TenantInvitations_Status DEFAULT N'Pending',
        AcceptedAt datetimeoffset(7) NULL,
        AcceptedByIssuer nvarchar(500) NULL,
        AcceptedBySubject nvarchar(200) NULL,
        RevokedAt datetimeoffset(7) NULL,
        CONSTRAINT FK_TenantInvitations_Tenants FOREIGN KEY (TenantId)
            REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
        CONSTRAINT UQ_TenantInvitations_TokenHash UNIQUE (TokenHash),
        CONSTRAINT CK_TenantInvitations_Role CHECK (Role IN (N'Admin',N'Editor',N'Analyst',N'Publisher')),
        CONSTRAINT CK_TenantInvitations_Status CHECK (Status IN (N'Pending',N'Accepted',N'Revoked'))
    );
    CREATE INDEX IX_TenantInvitations_Tenant_Status_Expiry
        ON dbo.TenantInvitations(TenantId, Status, ExpiresAt)
        INCLUDE (Email, Role, CreatedAt);
END;

IF OBJECT_ID(N'dbo.TenantSecurityAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TenantSecurityAudit
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantSecurityAudit PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        ActorIssuer nvarchar(500) NOT NULL,
        ActorSubject nvarchar(200) NOT NULL,
        EventType nvarchar(40) NOT NULL,
        TargetIssuer nvarchar(500) NULL,
        TargetSubject nvarchar(200) NULL,
        DetailsJson nvarchar(2000) NOT NULL CONSTRAINT DF_TenantSecurityAudit_Details DEFAULT N'{}',
        OccurredAt datetimeoffset(7) NOT NULL,
        CONSTRAINT FK_TenantSecurityAudit_Tenants FOREIGN KEY (TenantId)
            REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_TenantSecurityAudit_Tenant_OccurredAt
        ON dbo.TenantSecurityAudit(TenantId, OccurredAt DESC);
END;

COMMIT TRANSACTION;
