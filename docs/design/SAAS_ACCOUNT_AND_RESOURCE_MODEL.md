# SaaS account, workspace and resource model

## What is implemented in this branch

- OIDC Authorization Code + PKCE in the server-side Web app; CreatorFlow does not store passwords.
- Protected server cookie and CSRF-protected workspace selection/logout.
- Workspace listing and selection UI at `/account`.
- Workspace member invitation, role change and suspension UI for Owner/Admin where permitted by the API.
- API checks membership using the validated immutable `iss + sub` pair; the workspace header is only a selector.
- EF Core tenant query filters and tenant stamping for mapped business entities.
- Migration 022 creates a plan catalog, tenant subscription records, idempotent usage ledger and tenant-owned storage object metadata.

## Configuration required before login works

The Web app requires:
- `Authentication:Oidc:Authority`
- `Authentication:Oidc:ClientId`
- `Authentication:Oidc:ClientSecret`
- `Authentication:Oidc:ApiScope`

The API requires:
- `Authentication:Oidc:Authority`
- `Authentication:Oidc:Audience`
- `Authentication:OwnerBootstrapToken` for the one-time initial Owner bootstrap
- `ConnectionStrings:CreatorFlow`

Register the Web callback URLs from `docs/IDENTITY_SETUP.md` with the chosen identity provider. Use HTTPS. MFA and account recovery are provider responsibilities. Do not use the development API key as customer login.

## What is not yet a production SaaS feature

Migration 022 is a data model, not a complete billing or quota enforcement system. This branch does not yet integrate a payment provider, create/renew/cancel subscriptions automatically, seed commercial plan prices, or meter every AI/render/storage operation into the usage ledger. Do not advertise paid plans or hard quota enforcement until those services and their negative tests are complete.

The invitation API returns a one-time token; automatic SMTP/email delivery is not configured. Account/session revocation, MFA enforcement verification, and complete worker-level tenant propagation remain release gates.

## Tenant data migration warning

Migrations 011–013 are tenant foundation/lifecycle; migration 012 assigns existing listed business rows to the single `legacy-workspace`. Only apply this assignment if the target database is a single-workspace QC database and its ownership is confirmed. If it contains records for multiple customers, stop and create an explicit reviewed ownership mapping before migration 012.

The runner deliberately does not apply tenant migrations by default. After the target database is backed up and confirmed to be the single-workspace QC database, the explicit command is:

```powershell
.\database\Apply-Migrations.ps1 -SqlInstance localhost -Database CreatorFlow_QC -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment
```

This applies all outstanding migrations in the tenant-enabled sequence, including 021 and 022. Do not run it against Production or a database whose ownership is unknown.
