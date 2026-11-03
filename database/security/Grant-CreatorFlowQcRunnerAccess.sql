/*
    Least-privilege SQL access for the CreatorFlow-AI self-hosted QC runner.

    Expected Windows service identity: NT AUTHORITY\NETWORK SERVICE
    Scope: CreatorFlow_QC only.
    This script does NOT grant sysadmin, serveradmin, db_owner, or write permissions.
    Run once from SSMS as a SQL Server administrator on the local SQL Server host.
*/
USE [master];
GO

IF DB_ID(N'CreatorFlow_QC') IS NULL
    THROW 51000, 'CreatorFlow_QC does not exist. Initialize the QC database first.', 1;
GO

IF SUSER_ID(N'NT AUTHORITY\NETWORK SERVICE') IS NULL
    CREATE LOGIN [NT AUTHORITY\NETWORK SERVICE] FROM WINDOWS;
GO

USE [CreatorFlow_QC];
GO

IF USER_ID(N'NT AUTHORITY\NETWORK SERVICE') IS NULL
    CREATE USER [NT AUTHORITY\NETWORK SERVICE] FOR LOGIN [NT AUTHORITY\NETWORK SERVICE];
GO

IF IS_ROLEMEMBER(N'db_datareader', N'NT AUTHORITY\NETWORK SERVICE') <> 1
    ALTER ROLE [db_datareader] ADD MEMBER [NT AUTHORITY\NETWORK SERVICE];
GO

GRANT VIEW DEFINITION TO [NT AUTHORITY\NETWORK SERVICE];
GO

-- Fail closed if the runner identity accidentally has elevated server privileges.
USE [master];
GO
IF IS_SRVROLEMEMBER(N'sysadmin', N'NT AUTHORITY\NETWORK SERVICE') <> 0
    THROW 51001, 'Refusing: the QC runner identity must not be sysadmin.', 1;
GO

PRINT 'Least-privilege CreatorFlow_QC reader access granted to NT AUTHORITY\NETWORK SERVICE.';
GO
