# CreatorFlow-AI — Windows SQL Server QC Runner

**Host:** Windows 11 Pro, 32 GB RAM. **Database:** local SQL Server Developer. **Runner:** repository-scoped GitHub Actions, automatic on trusted branch pushes. **Only permitted target:** CreatorFlow_QC.

This is strictly a development/test environment. SQL Server Developer is licensed for development, testing and demonstration, not production. Do not place customer data, production backups, platform tokens or production credentials on this machine.

## 1. Topology and security boundary

- Install SQL Server 2022 Developer as a local default instance (localhost) or named instance (localhost\CREATORFLOW).
- Create one database named CreatorFlow_QC, tagged with database-level extended properties CreatorFlowEnvironment=QC and CreatorFlowRepository=CreatorFlow-AI.
- Run GitHub Actions service under a dedicated standard Windows account .\creatorflow-runner, not a local administrator.
- Map that Windows identity only to CreatorFlow_QC; grant DDL/data permissions inside that database, never server sysadmin or CREATE ANY DATABASE.
- Keep SQL traffic local. Do not expose TCP 1433 to the Internet or LAN.
- Use repository-level runner scope and label creatorflow-sql-qc.
- Workflow intentionally excludes pull_request; self-hosted runners have access to local resources. Only trusted repository writers should trigger jobs. For a public repository, enable required approval for external contributors and review Actions access settings before enabling this runner.

## 2. Install SQL Server

1. Apply Windows updates and reboot if required.
2. Download SQL Server 2022 from the Microsoft Evaluation Center: https://www.microsoft.com/en-us/evalcenter/evaluate-sql-server-2022/
3. Run setup.exe as Administrator → New SQL Server standalone installation → select Developer.
4. Select Database Engine Services only. Do not install Machine Learning Services, PolyBase, Analysis Services or other unrelated components for this runner.
5. Prefer the default instance (localhost). If named, use localhost\CREATORFLOW.
6. Select Windows Authentication and add your personal administrator account as SQL administrator.
7. Enable Shared Memory. TCP/IP can remain disabled for a local-only runner; if enabled, bind/restrict it to loopback and deny inbound firewall access.
8. Set SQL Server max server memory to about 20–22 GB initially, leaving RAM for Windows, IDE, runner and tests. Tune from measurements.
9. Apply the latest SQL Server 2022 servicing update after setup.

Install SSMS and sqlcmd separately using Microsoft's current installers. Install the PowerShell SQL module:

    Install-Module SqlServer -Scope AllUsers -Force -AllowClobber
    Get-Module -ListAvailable SqlServer
    Get-Command sqlcmd

Official installation steps: https://learn.microsoft.com/sql/database-engine/install-windows/install-sql-server-from-the-installation-wizard-setup

## 3. Create and tag the isolated database

In SSMS, connect to the local instance as SQL administrator and run once:

    IF DB_ID(N'CreatorFlow_QC') IS NULL
        CREATE DATABASE [CreatorFlow_QC];
    GO
    USE [CreatorFlow_QC];
    GO
    IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CreatorFlowEnvironment')
        EXEC sys.sp_updateextendedproperty @name=N'CreatorFlowEnvironment', @value=N'QC';
    ELSE
        EXEC sys.sp_addextendedproperty @name=N'CreatorFlowEnvironment', @value=N'QC';
    GO
    IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CreatorFlowRepository')
        EXEC sys.sp_updateextendedproperty @name=N'CreatorFlowRepository', @value=N'CreatorFlow-AI';
    ELSE
        EXEC sys.sp_addextendedproperty @name=N'CreatorFlowRepository', @value=N'CreatorFlow-AI';
    GO

Do not restore production data into this database. Use synthetic fixtures or sanitized development data only.

## 4. Create least-privilege Windows/SQL identity

Create a local standard Windows account creatorflow-runner through Computer Management. It must not be a member of Administrators or any privileged SQL/Windows group. Grant Log on as a service when installing the runner service.

In SSMS, replace YOURPC with the value of $env:COMPUTERNAME and run as SQL administrator:

    USE [master];
    IF SUSER_ID(N'YOURPC\creatorflow-runner') IS NULL
        CREATE LOGIN [YOURPC\creatorflow-runner] FROM WINDOWS;
    GO
    USE [CreatorFlow_QC];
    IF DATABASE_PRINCIPAL_ID(N'CreatorFlowQcRunner') IS NULL
        CREATE USER [CreatorFlowQcRunner]
          FOR LOGIN [YOURPC\creatorflow-runner] WITH DEFAULT_SCHEMA=[dbo];
    IF DATABASE_PRINCIPAL_ID(N'CreatorFlowQcRunnerRole') IS NULL
        CREATE ROLE [CreatorFlowQcRunnerRole];
    ALTER ROLE [db_ddladmin] ADD MEMBER [CreatorFlowQcRunnerRole];
    ALTER ROLE [db_datareader] ADD MEMBER [CreatorFlowQcRunnerRole];
    ALTER ROLE [db_datawriter] ADD MEMBER [CreatorFlowQcRunnerRole];
    GRANT EXECUTE TO [CreatorFlowQcRunnerRole];
    ALTER ROLE [CreatorFlowQcRunnerRole] ADD MEMBER [CreatorFlowQcRunner];
    GO
    SELECT IS_SRVROLEMEMBER(N'sysadmin', N'YOURPC\creatorflow-runner') AS IsSysAdmin;
    -- Expected: 0

These rights are scoped to the QC database. Do not grant CONTROL SERVER, sysadmin, or server-level database creation rights.

## 5. Register the GitHub runner

1. Open repository Nang0559/CreatorFlow-AI → Settings → Actions → Runners → New self-hosted runner.
2. Select Windows x64. Use the time-limited registration token and exact commands shown by GitHub at setup time.
3. Extract the runner to C:\actions-runner.
4. Configure labels including creatorflow-sql-qc.
5. Install it as a Windows service running as .\creatorflow-runner.
6. Verify it appears Online in repository Settings.

The service identity needs Modify rights only to the runner work directory. Keep runner registration repository-scoped, not organization-wide.

Official guide: https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners

## 6. Configure repository variable

In Settings → Secrets and variables → Actions → Variables, create:

| Variable | Example |
|---|---|
| CREATORFLOW_QC_SQL_INSTANCE | localhost or localhost\CREATORFLOW |

The workflow hardcodes the only allowed database as CreatorFlow_QC. Authentication uses Windows Integrated Security from the runner service account. Do not store a SQL administrator password or any production connection string in GitHub.

## 7. Run local preflight

From the repository root in PowerShell:

    $env:CREATORFLOW_QC_SQL_INSTANCE = 'localhost'
    $env:CREATORFLOW_QC_DATABASE = 'CreatorFlow_QC'
    .\tools\sql-qc\Test-SqlQcPreflight.ps1
    .\tools\sql-qc\Protect-QcDatabase.ps1
    .\tools\sql-qc\Invoke-SqlServerQc.ps1

Scripts included:

- Test-SqlQcPreflight.ps1: checks Windows x64, PowerShell, Git, .NET SDK, sqlcmd, SqlServer module, local instance and fixed DB name.
- Protect-QcDatabase.ps1: fails closed unless exact database name, ONLINE state, read-write state, both QC tags and non-sysadmin SQL principal are verified.
- Invoke-SqlServerQc.ps1: runs preflight + guard, SQL connectivity/version smoke query, then dotnet test CreatorFlow.sln -c Release.

## 8. Automatic workflow

.github/workflows/sqlserver-selfhosted-qc.yml triggers on trusted pushes to main and feature/** when source, tests, database, QC scripts, solution or workflow files change. It also supports manual workflow_dispatch.

The workflow serializes jobs because the initial setup uses one shared QC database. Do not run concurrent tests or reset operations against it.

**Important scope of current evidence:** the workflow proves the runner can connect to the tagged local QC database and that the current .NET test suite passes. It does not yet prove SQL migration 010/011 clean-install and upgrade parity, full schema verification, or SQL-backed business integration tests. Those must be implemented and run against this QC instance before calling database CI complete. The SQL smoke query is not a migration test.

## 9. Database protection and recovery policy

- Never add automatic DROP DATABASE, RESTORE DATABASE, broad cleanup, or repair commands to CI.
- Never let CI accept a configurable arbitrary database name; the guard allows only CreatorFlow_QC.
- If a safety check fails, stop and investigate. Do not weaken the guard to make the job green.
- Back up only disposable QC fixtures if needed. Never copy production/customer databases to this machine.
- Keep Windows, SQL Server, SSMS, runner and dependencies patched.
- If the QC database must be recreated, do it manually in SSMS after verifying its exact name and QC tags. Reapply the tag and least-privilege user scripts.

## 10. Official references

- SQL Server 2022 download: https://www.microsoft.com/en-us/evalcenter/evaluate-sql-server-2022/
- SQL Server installation guide: https://learn.microsoft.com/en-us/sql/database-engine/install-windows/install-sql-server
- SQL Server licensing guidance: https://www.microsoft.com/licensing/guidance/SQL
- GitHub self-hosted runner reference: https://docs.github.com/en/actions/reference/runners/self-hosted-runners
- GitHub self-hosted runner security guidance: https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners
