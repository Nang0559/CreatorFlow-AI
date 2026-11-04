using Microsoft.Data.SqlClient;

namespace CreatorFlow.Workers;

/// <summary>
/// Durable scheduler for topic discovery. Until a permitted platform adapter is configured,
/// runs are recorded as Skipped rather than fabricating search results or channel metrics.
/// </summary>
internal sealed class TopicDiscoveryWorker(
    IConfiguration configuration,
    ILogger<TopicDiscoveryWorker> logger) : BackgroundService
{
    private readonly string connectionString = configuration.GetConnectionString("CreatorFlow")
        ?? throw new InvalidOperationException("Missing SQL connection.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Topic discovery scheduler started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var run = await ClaimNextAsync(stoppingToken);
                if (run is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }

                // No TikTok Vietnam/Douyin connector is currently implemented in this worker.
                // Preserve the run and its error code so the UI can explain why no results appeared.
                await CompleteWithoutConnectorAsync(run, stoppingToken);
                logger.LogWarning("Topic discovery run {RunId} skipped: no approved platform connector is configured.", run.RunId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Topic discovery scheduler iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<ClaimedRun?> ClaimNextAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @claimed TABLE
            (
                RunId uniqueidentifier NOT NULL,
                ProfileId uniqueidentifier NOT NULL,
                PlatformsJson nvarchar(200) NOT NULL,
                IsScheduled bit NOT NULL
            );

            -- Recover abandoned work after a process crash; never leave a topic permanently locked.
            UPDATE h
            SET Status=N'Skipped',CompletedAt=SYSUTCDATETIME(),ErrorCode=N'WorkerLeaseExpired'
            FROM dbo.DiscoveryTopicRunHistory h
            INNER JOIN dbo.DiscoveryTopicProfiles p ON p.Id=h.TopicProfileId
            WHERE h.Status=N'Running' AND p.LeaseUntil IS NOT NULL
              AND p.LeaseUntil<SYSUTCDATETIME();

            UPDATE dbo.DiscoveryTopicProfiles
            SET LeaseUntil=NULL,LastRunStatus=N'Skipped',LastErrorCode=N'WorkerLeaseExpired'
            WHERE LeaseUntil IS NOT NULL AND LeaseUntil<SYSUTCDATETIME()
              AND LastRunStatus=N'Running';

            ;WITH manual AS
            (
                SELECT TOP(1) h.Id,h.TopicProfileId,h.PlatformsJson
                FROM dbo.DiscoveryTopicRunHistory h WITH(UPDLOCK,READPAST,ROWLOCK)
                INNER JOIN dbo.DiscoveryTopicProfiles p ON p.Id=h.TopicProfileId
                WHERE h.TriggerKind=N'Manual' AND h.Status=N'Queued'
                  AND (p.LeaseUntil IS NULL OR p.LeaseUntil<SYSUTCDATETIME())
                ORDER BY h.StartedAt,h.Id
            )
            UPDATE manual SET Status=N'Running',StartedAt=SYSUTCDATETIME()
            OUTPUT inserted.Id,inserted.TopicProfileId,inserted.PlatformsJson,CAST(0 AS bit)
            INTO @claimed(RunId,ProfileId,PlatformsJson,IsScheduled);

            IF NOT EXISTS(SELECT 1 FROM @claimed)
            BEGIN
                ;WITH due AS
                (
                    SELECT TOP(1) p.Id,p.PlatformsJson,p.RefreshMinutes
                    FROM dbo.DiscoveryTopicProfiles p WITH(UPDLOCK,READPAST,ROWLOCK)
                    INNER JOIN dbo.ContentTopics t ON t.Id=p.ContentTopicId
                    WHERE p.IsEnabled=1 AND p.NextRunAt<=SYSUTCDATETIME()
                      AND (p.LeaseUntil IS NULL OR p.LeaseUntil<SYSUTCDATETIME())
                      AND t.IsActive=1
                      AND NOT EXISTS
                      (
                        SELECT 1 FROM dbo.DiscoveryTopicRunHistory h
                        WHERE h.TopicProfileId=p.Id AND h.Status=N'Running'
                      )
                    ORDER BY p.NextRunAt,p.Id
                )
                UPDATE due
                SET LeaseUntil=DATEADD(minute,10,SYSUTCDATETIME()),
                    LastRunAt=SYSUTCDATETIME(),LastRunStatus=N'Running',
                    NextRunAt=DATEADD(minute,RefreshMinutes,SYSUTCDATETIME())
                OUTPUT NEWID(),inserted.Id,inserted.PlatformsJson,CAST(1 AS bit)
                INTO @claimed(RunId,ProfileId,PlatformsJson,IsScheduled);

                INSERT dbo.DiscoveryTopicRunHistory
                    (Id,TopicProfileId,RunKey,TriggerKind,Status,PlatformsJson,StartedAt)
                SELECT RunId,ProfileId,CONVERT(nvarchar(36),NEWID()),N'Schedule',N'Running',
                       PlatformsJson,SYSUTCDATETIME()
                FROM @claimed WHERE IsScheduled=1;
            END;

            UPDATE p SET LeaseUntil=DATEADD(minute,10,SYSUTCDATETIME()),
                LastRunAt=SYSUTCDATETIME(),LastRunStatus=N'Running',LastErrorCode=NULL
            FROM dbo.DiscoveryTopicProfiles p
            INNER JOIN @claimed c ON c.ProfileId=p.Id;

            SELECT RunId,ProfileId,PlatformsJson,IsScheduled FROM @claimed;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(ct);
            return null;
        }
        var run = new ClaimedRun(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetBoolean(3));
        await reader.DisposeAsync();
        await transaction.CommitAsync(ct);
        return run;
    }

    private async Task CompleteWithoutConnectorAsync(ClaimedRun run, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.DiscoveryTopicRunHistory
            SET Status=N'Skipped',CompletedAt=SYSUTCDATETIME(),ErrorCode=N'NoApprovedPublicConnector'
            WHERE Id=@runId AND Status=N'Running';

            UPDATE dbo.DiscoveryTopicProfiles
            SET LastRunStatus=N'Skipped',LastErrorCode=N'NoApprovedPublicConnector',LeaseUntil=NULL
            WHERE Id=@profileId;
            """;
        command.Parameters.AddWithValue("@runId", run.RunId);
        command.Parameters.AddWithValue("@profileId", run.ProfileId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record ClaimedRun(Guid RunId, Guid ProfileId, string PlatformsJson, bool IsScheduled);
}
