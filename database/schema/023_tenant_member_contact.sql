-- 023: retain only verified email contact data for workspace member administration.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.TenantMemberships',N'U') IS NULL
    THROW 52301, 'Migration 011 tenant membership foundation is required.', 1;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.TenantMemberships',N'VerifiedEmail') IS NULL
    ALTER TABLE dbo.TenantMemberships ADD VerifiedEmail nvarchar(320) NULL;
COMMIT TRANSACTION;
