-- Apply only to a dedicated CreatorFlow SQL Server database.
IF OBJECT_ID(N'dbo.ContentProjects', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ContentProjects
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ContentProjects PRIMARY KEY,
        Title nvarchar(200) NOT NULL,
        Status nvarchar(32) NOT NULL CONSTRAINT DF_ContentProjects_Status DEFAULT N'Draft',
        CreatedAt datetimeoffset(7) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_ContentProjects_Status CHECK
        (Status IN (N'Draft', N'Analyzing', N'ScriptReview', N'MediaReady',
                    N'Editing', N'FinalReview', N'Approved', N'Published',
                    N'Failed', N'Archived'))
    );
    CREATE INDEX IX_ContentProjects_Status_UpdatedAt ON dbo.ContentProjects(Status, UpdatedAt DESC);
END;
