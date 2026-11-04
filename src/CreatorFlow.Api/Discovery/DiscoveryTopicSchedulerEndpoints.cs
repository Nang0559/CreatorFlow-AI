using System.Text.Json;
using CreatorFlow.Api.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace CreatorFlow.Api.Discovery;

public static class DiscoveryTopicSchedulerEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryTopicSchedulerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/topics/{topicId:guid}/schedule").RequireAuthorization();

        group.MapGet("/", async (Guid topicId, ITenantContext tenant, IConfiguration config, CancellationToken ct) =>
        {
            if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
                return Results.Forbid();
            var cs = config.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);

            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP(1) p.Id,p.IsEnabled,p.RefreshMinutes,p.PlatformsJson,p.NextRunAt,
                       p.LastRunAt,p.LastRunStatus,p.LastErrorCode,p.LeaseUntil,
                       (SELECT TOP(1) h.CandidateCount FROM dbo.DiscoveryTopicRunHistory h
                         WHERE h.TenantId=p.TenantId AND h.TopicProfileId=p.Id
                         ORDER BY h.StartedAt DESC) AS CandidateCount,
                       (SELECT TOP(1) h.ChannelCount FROM dbo.DiscoveryTopicRunHistory h
                         WHERE h.TenantId=p.TenantId AND h.TopicProfileId=p.Id
                         ORDER BY h.StartedAt DESC) AS ChannelCount
                  FROM dbo.DiscoveryTopicProfiles p
                  JOIN dbo.ContentTopics t ON t.Id=p.ContentTopicId AND t.TenantId=p.TenantId
                 WHERE p.TenantId=@TenantId AND p.ContentTopicId=@TopicId;
                """;
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@TopicId", topicId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();
            return Results.Ok(new TopicScheduleResponse(
                reader.GetGuid(0), reader.GetBoolean(1), reader.GetInt32(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4),
                reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8),
                reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                reader.IsDBNull(10) ? 0 : reader.GetInt32(10)));
        });

        group.MapPut("/", async (Guid topicId, UpdateTopicScheduleRequest request, ITenantContext tenant,
            IConfiguration config, CancellationToken ct) =>
        {
            if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
                return Results.Forbid();
            if (request.RefreshMinutes is < 5 or > 1440)
                return Results.ValidationProblem(new Dictionary<string, string[]> {
                    ["refreshMinutes"] = ["Chu kỳ phải từ 5 đến 1.440 phút."]
                });

            var platforms = (request.Platforms ?? ["TikTokVietnam"])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (platforms.Length == 0 || platforms.Any(x => x is not ("TikTokVietnam" or "YouTubeShortsVietnam" or "FacebookReelsVietnam")))
                return Results.ValidationProblem(new Dictionary<string, string[]> {
                    ["platforms"] = ["Nền tảng hỗ trợ: TikTokVietnam, YouTubeShortsVietnam, FacebookReelsVietnam."]
                });

            var cs = config.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var json = JsonSerializer.Serialize(platforms);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM dbo.ContentTopics WITH (UPDLOCK,HOLDLOCK)
                               WHERE Id=@TopicId AND TenantId=@TenantId AND IsActive=1)
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT CAST(0 AS bit) AS TopicFound;
                    RETURN;
                END;
                IF EXISTS (SELECT 1 FROM dbo.DiscoveryTopicProfiles WITH (UPDLOCK,HOLDLOCK)
                           WHERE TenantId=@TenantId AND ContentTopicId=@TopicId)
                    UPDATE dbo.DiscoveryTopicProfiles
                       SET IsEnabled=@IsEnabled,RefreshMinutes=@RefreshMinutes,PlatformsJson=@PlatformsJson,
                           NextRunAt=CASE WHEN @IsEnabled=1 THEN
                               CASE WHEN IsEnabled=0 OR NextRunAt IS NULL THEN SYSUTCDATETIME()
                                    ELSE NextRunAt END ELSE NULL END,
                           LeaseUntil=CASE WHEN @IsEnabled=0 THEN NULL ELSE LeaseUntil END,
                           UpdatedAt=SYSUTCDATETIME()
                     WHERE TenantId=@TenantId AND ContentTopicId=@TopicId;
                ELSE
                    INSERT dbo.DiscoveryTopicProfiles
                        (Id,TenantId,ContentTopicId,TargetMarket,ProductContext,AudienceContext,
                         IsEnabled,RefreshMinutes,CreatedAt,UpdatedAt,PlatformsJson,NextRunAt)
                    VALUES
                        (NEWID(),@TenantId,@TopicId,N'Vietnam',N'',N'',@IsEnabled,@RefreshMinutes,
                         SYSUTCDATETIME(),SYSUTCDATETIME(),@PlatformsJson,
                         CASE WHEN @IsEnabled=1 THEN SYSUTCDATETIME() ELSE NULL END);
                COMMIT TRANSACTION;
                SELECT CAST(1 AS bit) AS TopicFound;
                """;
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@TopicId", topicId);
            command.Parameters.AddWithValue("@IsEnabled", request.IsEnabled);
            command.Parameters.AddWithValue("@RefreshMinutes", request.RefreshMinutes);
            command.Parameters.AddWithValue("@PlatformsJson", json);
            var found = (bool?)await command.ExecuteScalarAsync(ct) ?? false;
            if (!found) return Results.NotFound();
            return Results.Ok(new { saved = true, isEnabled = request.IsEnabled, refreshMinutes = request.RefreshMinutes, platforms });
        });

        group.MapGet("/history", async (Guid topicId, int? take, ITenantContext tenant,
            IConfiguration config, CancellationToken ct) =>
        {
            if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
                return Results.Forbid();
            var size = Math.Clamp(take ?? 20, 1, 100);
            var cs = config.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP(@Take) h.Id,h.TriggerKind,h.Status,h.StartedAt,h.CompletedAt,
                       h.CandidateCount,h.ChannelCount,h.ErrorCode
                  FROM dbo.DiscoveryTopicRunHistory h
                  JOIN dbo.DiscoveryTopicProfiles p ON p.Id=h.TopicProfileId AND p.TenantId=h.TenantId
                 WHERE h.TenantId=@TenantId AND p.ContentTopicId=@TopicId
                 ORDER BY h.StartedAt DESC;
                """;
            command.Parameters.AddWithValue("@Take", size);
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@TopicId", topicId);
            var rows = new List<TopicScheduleRunResponse>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(new TopicScheduleRunResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.GetDateTimeOffset(3), reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4),
                    reader.GetInt32(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
            return Results.Ok(rows);
        });

        return app;
    }
}

public sealed record UpdateTopicScheduleRequest(bool IsEnabled, int RefreshMinutes, string[]? Platforms);
public sealed record TopicScheduleResponse(Guid Id, bool IsEnabled, int RefreshMinutes, string PlatformsJson,
    DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, string? LastRunStatus, string? LastErrorCode,
    DateTimeOffset? LeaseUntil, int CandidateCount, int ChannelCount);
public sealed record TopicScheduleRunResponse(Guid Id, string TriggerKind, string Status,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int CandidateCount, int ChannelCount, string? ErrorCode);
