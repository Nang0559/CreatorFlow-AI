# CreatorFlow-AI — Windows SQL Server QC Runner

**Host:** Windows 11 Pro, 32 GB RAM. **Database:** local SQL Server Developer. **Runner:** repository-scoped GitHub Actions, automatic on trusted branch pushes. **Only permitted target:** CreatorFlow_QC.

This is strictly a development/test environment. SQL Server Developer is licensed for development, testing and demonstration, not production. Do not place customer data, production backups, platform tokens or production credentials on this machine.

## 1. Topology and security boundary

- Install SQL Server 2022+ as a local default instance (localhost) or named instance (localhost\CREATORFLOW).
- Provision `CreatorFlow_QC` using [the repository database initializer](SQL_DATABASE_SETUP.md). It is tagged with database-level extended properties `CreatorFlowEnvironment=QC` and `CreatorFlowRepository=CreatorFlow-AI`.
- Run GitHub Actions service under a dedicated standard Windows account `.\creatorflow-runner`, not a local administrator.
- Map that Windows identity only to CreatorFlow_QC; grant DDL/data permissions inside that database, never server sysadmin or CREATE ANY DATABASE.
- Keep SQL traffic local. Do not expose TCP 1433 to the Internet or LAN.
- Use repository-level runner scope and label `creatorflow-sql-qc`.
- Workflow intentionally excludes `pull_request`; self-hosted runners have access to local resources. Only trusted repository writers should trigger jobs. For a public repository, enable required approval for external contributors and review Actions access settings before enabling this runner.

## 2. Install SQL Server

1. Apply Windows updates and reboot if required.
2. Install SQL Server Developer 2022 or later. SQL Server 2025 Developer is suitable for this local development/QC role.
3. Select Database Engine Services only. Do not install unrelated components for this runner.
4. Prefer the default instance (localhost). If named, use localhost\CREATORFLOW.
5. Select Windows Authentication and add your personal administrator account as SQL administrator.
6. Enable Shared Memory. TCP/IP can remain disabled for a local-only runner; if enabled, bind/restrict it to loopback and deny inbound firewall access.
7. Set SQL Server max server memory to about 20–22 GB initially, leaving RAM for Windows, IDE, runner and tests. Tune from measurements.
8. Apply the latest SQL Server servicing update after setup.

Install SSMS and sqlcmd separately using Microsoft's current installers. Install the PowerShell SQL module:

```powershell
Install-Module SqlServer -Scope AllUsers -Force -AllowClobber
Get-Module -ListAvailable SqlServer
Get-Command sqlcmd
```

## 3. Provision the database and schema

Run PowerShell as a SQL administrator, from the repository root:

```powershell
.\database\Initialize-CreatorFlowDatabase.ps1 -SqlInstance localhost -IncludeTenantMigrations -ConfirmLegacyWorkspaceAssignment
```

This creates and tags only `CreatorFlow_QC`, applies migrations 001–020 (including tenant migrations 011–013), records migration names and SHA-256 checksums, then verifies required tables and tenant constraints. It does not drop or restore a database. If `CreatorFlow_QC` already exists, it must already carry both exact QC tags; an unknown database is never silently adopted.

Migration 012 assigns existing rows in its listed single-workspace tables to `legacy-workspace`. Review this ownership assumption before using tenant migrations on any database containing multi-customer data.

## 4. Grant least-privilege SQL access to the existing Z240 runner

The currently registered Z240 runner service has been observed running as `NT AUTHORITY\NETWORK SERVICE`. The SQL QC workflow does not need schema-write privileges: it checks the already initialized schema and runs the .NET test suite. Do not grant this service sysadmin, db_owner, db_ddladmin or db_datawriter.

After confirming the runner service identity in Windows Services, run the following file once from SSMS as a SQL administrator on the local SQL Server host:

```sql
-- Open and execute database/security/Grant-CreatorFlowQcRunnerAccess.sql
```

The script:
- Creates the Windows login only if missing.
- Maps it only into `CreatorFlow_QC`.
- Grants `db_datareader` and database `VIEW DEFINITION` only.
- Explicitly refuses a sysadmin identity.
- Does not alter migrations, schema, production databases or business data.

If the runner service is later changed to a dedicated local account (recommended for long-term hardening), replace the principal in that script with the exact service identity and re-run the least-privilege setup. Never grant permissions to an assumed identity without first checking the service's actual `StartName`.

## 5. Register the GitHub runner

1. Open repository Nang0559/CreatorFlow-AI → Settings → Actions → Runners → New self-hosted runner.
2. Select Windows x64. Use the time-limited registration token and exact commands shown by GitHub at setup time.
3. Extract the runner to C:\actions-runner.
4. Configure the custom labels `pctp-ui` and `sql-network` (along with GitHub's default `self-hosted` and `windows` labels).
5. Install it as a Windows service running as `.\creatorflow-runner`.
6. Verify it appears Online in repository Settings.

The service identity needs Modify rights only to the runner work directory. Keep runner registration repository-scoped, not organization-wide.

## 6. Configure repository variable

In Settings → Secrets and variables → Actions → Variables, create:

| Variable | Example |
|---|---|
| CREATORFLOW_QC_SQL_INSTANCE | localhost or localhost\CREATORFLOW |

The workflow hardcodes the only allowed database as CreatorFlow_QC. Authentication uses Windows Integrated Security from the runner service account. Do not store a SQL administrator password or any production connection string in GitHub.

## 7. Run local QC

From the repository root in PowerShell, using the dedicated non-sysadmin runner identity:

```powershell
$env:CREATORFLOW_QC_SQL_INSTANCE = 'localhost'
$env:CREATORFLOW_QC_DATABASE = 'CreatorFlow_QC'
.\tools\sql-qc\Test-SqlQcPreflight.ps1
.\tools\sql-qc\Protect-QcDatabase.ps1
.\database\Verify-Database.ps1 -RequireTenantMigrations
.\tools\sql-qc\Invoke-SqlServerQc.ps1
```

Scripts included:

- `Test-SqlQcPreflight.ps1`: checks Windows x64, PowerShell, Git, .NET SDK, sqlcmd, SqlServer module, local instance and fixed DB name.
- `Protect-QcDatabase.ps1`: fails closed unless exact database name, ONLINE state, read-write state, both QC tags and non-sysadmin SQL principal are verified.
- `database/Verify-Database.ps1`: checks all 20 migration checksums, required schema tables, tenant columns, trusted composite foreign keys and workspace-scoped unique indexes.
- `Invoke-SqlServerQc.ps1`: runs preflight + guard + schema verification, SQL connectivity/version smoke query, then `dotnet test CreatorFlow.sln -c Release`.

## 8. Automatic workflow

`.github/workflows/sqlserver-selfhosted-qc.yml` triggers on trusted pushes to main and feature/** when source, tests, database, QC scripts, solution or workflow files change. It also supports manual workflow_dispatch. It targets the exact runner labels `self-hosted`, `windows`, `pctp-ui`, and `sql-network`.

The workflow serializes jobs because the initial setup uses one shared QC database. Do not run concurrent tests or reset operations against it.

**Evidence boundary:** the workflow verifies the runner can connect to the tagged local QC database and that all 20 recorded migrations match the repository schema files, then runs the .NET test suite. It does not yet claim full SQL-backed business integration coverage unless dedicated SQL scenario tests are added and executed.

## 9. Database protection and recovery policy

- Never add automatic DROP DATABASE, RESTORE DATABASE, broad cleanup, or repair commands to CI.
- Never let CI accept a configurable arbitrary database name; the guard allows only CreatorFlow_QC.
- If a safety check fails, stop and investigate. Do not weaken the guard to make the job green.
- Back up only disposable QC fixtures if needed. Never copy production/customer databases to this machine.
- If the QC database must be recreated, do it manually after verifying its exact name and QC tags. Reapply the least-privilege user setup after initialization.

## 10. Official references

- SQL Server installation guide: https://learn.microsoft.com/en-us/sql/database-engine/install-windows/install-sql-server
- SQL Server licensing guidance: https://www.microsoft.com/licensing/guidance/SQL
- GitHub self-hosted runner reference: https://docs.github.com/en/actions/reference/runners/self-hosted-runners
- GitHub self-hosted runner security guidance: https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners
