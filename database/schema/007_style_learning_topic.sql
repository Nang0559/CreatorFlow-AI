-- Add topic-level attribution for style learning without rewriting historical experiments.
IF COL_LENGTH(N'dbo.StyleExperiments', N'Topic') IS NULL
    ALTER TABLE dbo.StyleExperiments ADD Topic nvarchar(200) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StyleExperiments_Topic_Status' AND object_id = OBJECT_ID(N'dbo.StyleExperiments'))
    CREATE INDEX IX_StyleExperiments_Topic_Status
        ON dbo.StyleExperiments(Topic, Status, UpdatedAt DESC);
