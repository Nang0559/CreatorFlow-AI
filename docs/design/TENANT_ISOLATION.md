# Tenant identity and data isolation

## Security model

- A tenant is a workspace. A user may have memberships in multiple workspaces.
- OIDC access tokens must be validated by the configured authority (signature, issuer, audience and lifetime).
- The active workspace is selected with `X-CreatorFlow-Tenant`; this header is only a selector. The API resolves it against the exact `iss + sub + TenantId` active membership and an active Tenant row.
- Email, display name, request-body TenantId and the tenant header alone are never identity or authorization evidence.
- All authenticated API endpoints use the default authorization policy, which now requires a resolved active tenant membership.
- The API-key handler is local-development bootstrap only. It maps to the fixed legacy workspace and is not a customer identity or supported production authentication method.
- EF Core tenant query filters apply to every business entity; inserts are stamped with the resolved TenantId and updates cannot move a row between tenants.

## Data migration

Apply migrations in order through 011, then 012. Migration 012 adds a non-null TenantId and tenant FK/index to each current business table. Existing rows are preserved and assigned to the single `legacy-workspace` tenant (ID `00000000-0000-4000-8000-000000000001`). This is valid only when the pre-migration database represents one workspace. If the database already contains multiple customers, stop and perform a reviewed ownership mapping before applying migration 012.

New OIDC users are not automatically members. A trusted operator must create TenantMembership rows using the exact issuer and immutable subject from the validated identity provider. Do not use email as SubjectId. Tenant membership provisioning should be added to a controlled invitation/onboarding workflow before public SaaS onboarding.

## Configuration

Set:

- `Authentication:Oidc:Authority` — trusted OIDC issuer/authority.
- `Authentication:Oidc:Audience` — API audience.
- `Tenant:AllowApiKeyBootstrap=false` outside local development.
- `Tenant:BootstrapTenantId=00000000-0000-4000-8000-000000000001` only for local development.

Every OIDC API request must include `X-CreatorFlow-Tenant: <tenant-guid>`. The API checks the selected tenant against active membership on every request.

## Explicit limitations / launch blockers

- This is the first enforced identity and row-isolation layer, not a complete customer onboarding product.
- Tenant invitations, membership administration APIs, MFA policy and account recovery still require an identity-provider/BFF workflow.
- Existing unique indexes that are globally unique may need tenant-local redesign after production query patterns are measured; this can restrict duplicate names across tenants but does not grant cross-tenant reads.
- SQL Server Row-Level Security is defense in depth and is not yet configured. Application EF filters and API membership checks are mandatory.
- Background workers must create an explicit tenant context per job before querying or writing tenant-owned rows.
- SQL migration 012 must be exercised on empty and populated SQL Server databases; do not infer SQL Server validation from unit tests.
