-- Operator-controlled membership provisioning template.
-- Obtain Issuer and SubjectId from a successfully validated OIDC identity; never use email as SubjectId.
-- Execute only through a privileged, audited DBA/operator session.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @TenantId uniqueidentifier = NULL; -- replace with an existing Tenant.Id
DECLARE @Issuer nvarchar(500) = NULL;       -- exact OIDC iss claim, including any trailing slash
DECLARE @SubjectId nvarchar(200) = NULL;    -- exact immutable OIDC sub claim
DECLARE @Role nvarchar(16) = N'Owner';      -- Owner, Admin, Editor, Analyst, Publisher
DECLARE @Now datetimeoffset(7) = SYSUTCDATETIME();

IF @TenantId IS NULL OR @Issuer IS NULL OR LEN(LTRIM(RTRIM(@Issuer))) = 0
   OR @SubjectId IS NULL OR LEN(LTRIM(RTRIM(@SubjectId))) = 0
    THROW 51210, 'TenantId, exact OIDC issuer and subject are required.', 1;

IF @Role NOT IN (N'Owner', N'Admin', N'Editor', N'Analyst', N'Publisher')
    THROW 51211, 'Unsupported tenant membership role.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = @TenantId AND Status = N'Active')
    THROW 51212, 'Target tenant does not exist or is not active.', 1;

IF EXISTS
(
    SELECT 1 FROM dbo.TenantMemberships
    WHERE TenantId = @TenantId AND IdentityIssuer = LTRIM(RTRIM(@Issuer))
      AND SubjectId = LTRIM(RTRIM(@SubjectId))
)
    THROW 51213, 'Membership already exists; use a reviewed role/status change workflow.', 1;

INSERT dbo.TenantMemberships
    (Id, TenantId, IdentityIssuer, SubjectId, Role, Status, CreatedAt, UpdatedAt)
VALUES
    (NEWID(), @TenantId, LTRIM(RTRIM(@Issuer)), LTRIM(RTRIM(@SubjectId)),
     @Role, N'Active', @Now, @Now);

COMMIT TRANSACTION;
