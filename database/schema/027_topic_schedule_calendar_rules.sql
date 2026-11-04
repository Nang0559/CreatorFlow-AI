-- 027: support interval, daily and weekly topic schedules in the workspace time zone.
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.DiscoveryTopicProfiles',N'U') IS NULL
    THROW 52701, 'Migration 025 topic scheduler is required.', 1;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'ScheduleMode') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD ScheduleMode nvarchar(12) NOT NULL
        CONSTRAINT DF_DiscoveryTopicProfiles_ScheduleMode DEFAULT N'Interval';
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'LocalTime') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD LocalTime nvarchar(5) NOT NULL
        CONSTRAINT DF_DiscoveryTopicProfiles_LocalTime DEFAULT N'09:00';
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'DaysOfWeekJson') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD DaysOfWeekJson nvarchar(200) NOT NULL
        CONSTRAINT DF_DiscoveryTopicProfiles_DaysOfWeek DEFAULT N'[]';
IF COL_LENGTH(N'dbo.DiscoveryTopicProfiles',N'TimeZoneId') IS NULL
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD TimeZoneId nvarchar(80) NOT NULL
        CONSTRAINT DF_DiscoveryTopicProfiles_TimeZone DEFAULT N'Asia/Ho_Chi_Minh';
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DiscoveryTopicProfiles_ScheduleMode')
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD CONSTRAINT CK_DiscoveryTopicProfiles_ScheduleMode
        CHECK(ScheduleMode IN(N'Interval',N'Daily',N'Weekly'));
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DiscoveryTopicProfiles_LocalTime')
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD CONSTRAINT CK_DiscoveryTopicProfiles_LocalTime
        CHECK(LocalTime LIKE '[0-2][0-9]:[0-5][0-9]');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name=N'CK_DiscoveryTopicProfiles_DaysOfWeekJson')
    ALTER TABLE dbo.DiscoveryTopicProfiles ADD CONSTRAINT CK_DiscoveryTopicProfiles_DaysOfWeekJson
        CHECK(ISJSON(DaysOfWeekJson)=1);
COMMIT TRANSACTION;
