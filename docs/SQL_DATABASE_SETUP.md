# SQL Server database setup

This runbook provisions the repository's isolated local QC database and applies its schema migrations.

## Scope and safety

- Scripts are restricted to a local SQL Server instance and the exact database name `CreatorFlow_QC`.
- SQL Server Developer is for development, testing and demonstration, not production.
- The create step never drops, restores, overwrites or silently retags an existing database.
- An existing database must already carry both database-level tags: `CreatorFlowEnvironment=QC` and `CreatorFlowRepository=CreatorFlow-AI`.
- Migration execution refuses to adopt a database that has user tables but no `dbo.SchemaMigrations` ledger.
- CI does not provision databases or execute migrations. Provisioning requires an administrator; the self-hosted QC runner remains a non-sysadmin principal.

## Prerequisites

1. Local SQL Server 2022 or later. SQL Server 2025 is a supported newer engine target for this development/QC setup.
2. PowerShell 5.1 or later.
3. SQL Server PowerShell module:

   ```powershell
   Install-Module SqlServer -Scope AllUsers -Force -AllowClobber
   ```

4. Run initialization from a Windows account allowed to create databases and execute schema DDL.

## Initialize the complete schema

From the repository root:

```powershell
.\database\Initialize-CreatorFlowDatabase.ps1 -SqlInstance localhost -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment
```

The initializer runs three stages:

1. `Create-Database.ps1`: creates `CreatorFlow_QC` if absent and tags it. If it already exists, exact tags and ONLINE/read-write state must be present; existing tags are not changed.
2. `Apply-Migrations.ps1`: applies non-tenant migrations 001–010 and 014–015. When tenant migrations 011–013 are explicitly requested (or already fully installed), it also applies tenant-dependent migrations 016–020. Each applied file is recorded with its SHA-256 checksum.
3. `Verify-Database.ps1`: checks migration history against repository files, required tables, tenant ownership constraints, trusted composite foreign keys and workspace-scoped unique indexes.

Migration 012 explicitly assigns existing rows in its tenant-owned table list to `legacy-workspace`. The confirmation switch is mandatory whenever tenant migrations are requested. This is suitable for a fresh, single-workspace development database. Do not use it to infer ownership for a database already containing multiple customers.

For the non-tenant baseline (001–010 and 014–015), run:

```powershell
.\database\Initialize-CreatorFlowDatabase.ps1 -SqlInstance localhost
```

## Run verification independently

```powershell
.\database\Verify-Database.ps1 -SqlInstance localhost -RequireTenantMigrations
```

Omit `-RequireTenantMigrations` to verify only non-tenant migrations 001–010 and 014–015. Share Inbox and video features require tenant migrations 011–013 and migrations 016–020.

## Re-running and migration immutability

The initializer can be re-run for recorded migrations: it verifies exact file names and SHA-256 checksums, then skips matching entries. If an applied migration file changes, execution stops and requires a new numbered migration. A failed migration is not recorded as applied; inspect the SQL error before retrying.

The migration ledger is not a substitute for backups or review. Never use these scripts against production or customer databases.

## QC runner integration

After provisioning, configure the runner service identity with the database-scoped permissions documented in [SQL Server QC runner](SQLSERVER_QC_RUNNER.md). The QC runner verifies 17 non-tenant migrations plus 011–013 when tenant mode is required, including tenant metadata and composite relationship constraints, before running .NET tests. It does not create, drop, restore or migrate the database.
