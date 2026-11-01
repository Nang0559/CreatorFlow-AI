-- Immutable time-series metric observations for experiment learning and ML datasets.
-- Apply after schema scripts 001-007.
IF OBJECT_ID(N'dbo.StyleVariantMetricObservations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StyleVariantMetricObservations
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_StyleVariantMetricObservations PRIMARY KEY,
        StyleExperimentVariantId uniqueidentifier NOT NULL,
        ObservedAt datetimeoffset(7) NOT NULL,
        Views bigint NOT NULL,
        Likes bigint NOT NULL,
        Comments bigint NOT NULL,
        Shares bigint NOT NULL,
        Saves bigint NOT NULL,
        FollowsGained bigint NOT NULL,
        ProductClicks bigint NOT NULL,
        Orders bigint NOT NULL,
        AverageWatchSeconds decimal(12,3) NULL,
        VideoDurationSeconds decimal(12,3) NULL,
        CONSTRAINT FK_StyleVariantMetricObservations_Variants FOREIGN KEY (StyleExperimentVariantId)
            REFERENCES dbo.StyleExperimentVariants(Id) ON DELETE CASCADE,
        CONSTRAINT UQ_StyleVariantMetricObservations_Variant_ObservedAt UNIQUE (StyleExperimentVariantId, ObservedAt),
        CONSTRAINT CK_StyleVariantMetricObservations_Metrics CHECK
            (Views >= 0 AND Likes >= 0 AND Comments >= 0 AND Shares >= 0 AND Saves >= 0
             AND FollowsGained >= 0 AND ProductClicks >= 0 AND Orders >= 0),
        CONSTRAINT CK_StyleVariantMetricObservations_WatchTime CHECK
            ((AverageWatchSeconds IS NULL OR AverageWatchSeconds >= 0)
             AND (VideoDurationSeconds IS NULL OR VideoDurationSeconds >= 0)
             AND (AverageWatchSeconds IS NULL OR VideoDurationSeconds IS NULL OR AverageWatchSeconds <= VideoDurationSeconds))
    );
    CREATE INDEX IX_StyleVariantMetricObservations_ObservedAt
        ON dbo.StyleVariantMetricObservations(ObservedAt DESC, StyleExperimentVariantId);
END;
