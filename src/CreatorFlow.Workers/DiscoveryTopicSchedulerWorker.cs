using Microsoft.Data.SqlClient;

namespace CreatorFlow.Workers;

/// <summary>
/// Claims due topic schedules with SQL Server row locks and a durable lease.
/// Until an approved read-only discovery connector is configured, the run is
/// recorded as Skipped rather than fabricating candidates or channel counts.
/// </summary>
internal sealed class DiscoveryTopicSchedulerWorker(
    IConfiguration config,
    ILogger<DiscoveryTopicSchedulerWorker> logger) : BackgroundService
{
    private readonly string? connectionString = config.GetConnectionString("CreatorFlow");
    private readonly int pollSeconds = Math.Clamp(config.GetValue("DiscoveryScheduler:PollSeconds", 15), 5, 300);
    private readonly int leaseMinutes = Math.Clamp(config.GetValue("DiscoveryScheduler:LeaseMinutes", 5), 1, 30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("Bộ lập lịch khám phá chưa chạy: thiếu ConnectionStrings:CreatorFlow.");
            return;
        }

        logger.LogInformation("Bộ lập lịch khám phá đã khởi động; chu kỳ kiểm tra {PollSeconds}s.", pollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await ClaimDueTopicAsync(stoppingToken);
                if (!claimed)
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi khi lấy chủ đề đến hạn trong bộ lập lịch khám phá.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(pollSeconds, 15)), stoppingToken);
            }
        }
    }

    private async Task<bool> ClaimDueTopicAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        var profileId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var runKey = $"schedule:{Guid.NewGuid():N}";
        Guid tenantId;
        Guid topicId;
        string topicName;
        string platformsJson;
        int refreshMinutes;

        await using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE h
                   SET Status=N'Failed',CompletedAt=SYSUTCDATETIME(),ErrorCode=N'LeaseExpired'
                  FROM dbo.DiscoveryTopicRunHistory h
                  INNER JOIN dbo.DiscoveryTopicProfiles p
                    ON p.Id=h.TopicProfileId AND p.TenantId=h.TenantId
                 WHERE h.Status=N'Running' AND p.LeaseUntil < SYSUTCDATETIME();

                ;WITH due AS
                (
                    SELECT TOP (1) p.Id
                    FROM dbo.DiscoveryTopicProfiles p WITH (UPDLOCK, READPAST, ROWLOCK)
                    INNER JOIN dbo.ContentTopics t ON t.Id=p.ContentTopicId AND t.TenantId=p.TenantId
                    WHERE p.IsEnabled=1
                      AND t.IsActive=1
                      AND p.NextRunAt <= SYSUTCDATETIME()
                      AND (p.LeaseUntil IS NULL OR p.LeaseUntil < SYSUTCDATETIME())
                    ORDER BY p.NextRunAt, p.Id
                )
                UPDATE p
                   SET LeaseUntil=DATEADD(minute,@LeaseMinutes,SYSUTCDATETIME()),
                       LastRunStatus=N'Running',
                       LastErrorCode=NULL,
                       UpdatedAt=SYSUTCDATETIME()
                OUTPUT inserted.Id,inserted.TenantId,inserted.ContentTopicId,
                       inserted.PlatformsJson,inserted.RefreshMinutes
                  FROM dbo.DiscoveryTopicProfiles p
                  INNER JOIN due ON due.Id=p.Id;
                """;
            claim.Parameters.AddWithValue("@LeaseMinutes", leaseMinutes);
            await using var reader = await claim.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(ct);
                return false;
            }

            profileId = reader.GetGuid(0);
            tenantId = reader.GetGuid(1);
            topicId = reader.GetGuid(2);
            platformsJson = reader.GetString(3);
            refreshMinutes = reader.GetInt32(4);
        }

        await using (var topic = connection.CreateCommand())
        {
            topic.Transaction = transaction;
            topic.CommandText = "SELECT Name FROM dbo.ContentTopics WHERE Id=@TopicId AND TenantId=@TenantId AND IsActive=1;";
            topic.Parameters.AddWithValue("@TopicId", topicId);
            topic.Parameters.AddWithValue("@TenantId", tenantId);
            topicName = (string?)await topic.ExecuteScalarAsync(ct) ?? string.Empty;
        }

        await using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                INSERT dbo.DiscoveryTopicRunHistory
                    (Id,TenantId,TopicProfileId,RunKey,TriggerKind,Status,PlatformsJson,StartedAt,
                     CandidateCount,ChannelCount,ErrorCode)
                VALUES
                    (@Id,@TenantId,@ProfileId,@RunKey,N'Schedule',N'Running',@PlatformsJson,
                     SYSUTCDATETIME(),0,0,NULL);
                """;
            history.Parameters.AddWithValue("@Id", runId);
            history.Parameters.AddWithValue("@TenantId", tenantId);
            history.Parameters.AddWithValue("@ProfileId", profileId);
            history.Parameters.AddWithValue("@RunKey", runKey);
            history.Parameters.AddWithValue("@PlatformsJson", platformsJson);
            await history.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);

        // Do not represent an AI query plan as collected trend data. A source adapter
        // must be explicitly approved and installed before this worker can fetch candidates.
        const string connectorMissing = "NoApprovedSourceConnector";
        await using var finish = connection.CreateCommand();
        finish.CommandText = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.DiscoveryTopicRunHistory
               SET Status=N'Skipped',CompletedAt=SYSUTCDATETIME(),ErrorCode=@ErrorCode
             WHERE Id=@RunId AND TenantId=@TenantId AND Status=N'Running';
            UPDATE dbo.DiscoveryTopicProfiles
               SET LastRunAt=SYSUTCDATETIME(),LastRunStatus=N'Skipped',
                   LastErrorCode=@ErrorCode,LeaseUntil=NULL,
                   NextRunAt=CASE WHEN IsEnabled=1 THEN DATEADD(minute,RefreshMinutes,SYSUTCDATETIME()) ELSE NULL END,
                   UpdatedAt=SYSUTCDATETIME()
             WHERE Id=@ProfileId AND TenantId=@TenantId;
            COMMIT TRANSACTION;
            """;
        finish.Parameters.AddWithValue("@ErrorCode", connectorMissing);
        finish.Parameters.AddWithValue("@RunId", runId);
        finish.Parameters.AddWithValue("@ProfileId", profileId);
        finish.Parameters.AddWithValue("@TenantId", tenantId);
        await finish.ExecuteNonQueryAsync(ct);

        logger.LogWarning(
            "Đã xử lý lịch chủ đề {TopicName} ({ProfileId}) nhưng bỏ qua thu thập: chưa có connector nguồn được phê duyệt. Chu kỳ tiếp theo {RefreshMinutes} phút.",
            topicName, profileId, refreshMinutes);
        return true;
    }
}
