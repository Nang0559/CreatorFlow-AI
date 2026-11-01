-- CreatorFlow SaaS identity foundation.
-- This migration deliberately does NOT retrofit TenantId onto legacy business tables.
-- No tenant-facing data endpoints may be enabled until each owned table is scoped and tested.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Tenants
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
        Name nvarchar(160) NOT NULL,
        Slug nvarchar(80) NOT NULL,
        Status nvarchar(16) NOT NULL CONSTRAINT DF_Tenants_Status DEFAULT N'Active',
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT UQ_Tenants_Slug UNIQUE (Slug),
        CONSTRAINT CK_Tenants_Status CHECK (Status IN (N'Active', N'Suspended')),
        CONSTRAINT CK_Tenants_Slug CHECK
        (
            LEN(Slug) BETWEEN 3 AND 80
            AND Slug NOT LIKE N'-%'
            AND Slug NOT LIKE N'%-'
            AND Slug NOT LIKE N'%[^a-z0-9-]%'
            AND Slug NOT LIKE N'%--%'
        )
    );
END;

IF OBJECT_ID(N'dbo.TenantMemberships', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TenantMemberships
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_TenantMemberships PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        IdentityIssuer nvarchar(500) NOT NULL,
        SubjectId nvarchar(200) NOT NULL,
        Role nvarchar(16) NOT NULL,
        Status nvarchar(16) NOT NULL CONSTRAINT DF_TenantMemberships_Status DEFAULT N'Active',
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT FK_TenantMemberships_Tenants FOREIGN KEY (TenantId)
            REFERENCES dbo.Tenants(Id) ON DELETE NO ACTION,
        CONSTRAINT UQ_TenantMemberships_TenantIssuerSubject UNIQUE (TenantId, IdentityIssuer, SubjectId),
        CONSTRAINT CK_TenantMemberships_Role CHECK (Role IN (N'Owner',N'Admin',N'Editor',N'Analyst',N'Publisher')),
        CONSTRAINT CK_TenantMemberships_Status CHECK (Status IN (N'Active',N'Suspended'))
    );
    CREATE INDEX IX_TenantMemberships_IssuerSubject_Status
        ON dbo.TenantMemberships(IdentityIssuer, SubjectId, Status)
        INCLUDE (TenantId, Role);
END;

COMMIT TRANSACTION;
