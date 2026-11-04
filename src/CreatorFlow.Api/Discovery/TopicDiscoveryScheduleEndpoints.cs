using System.Text.Json;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace CreatorFlow.Api.Discovery;

public static class TopicDiscoveryScheduleEndpoints
{
    private static readonly string[] AllowedIntervals = ["60", "180", "360", "720", "1440"];
    private static readonly HashSet<string> AllowedPlatforms = new(StringComparer.Ordinal)
        { "TikTokVietnam", "Douyin" };

    public static IEndpointRouteBuilder MapTopicDiscoveryScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/topics").RequireAuthorization();

        group.MapGet("/{topicId:guid}/schedule", async (
            Guid topicId, ITenantContext tenant, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!TryGetTenant(tenant, out var tenantId)) return Results.Forbid();
            var cs = configuration.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.Id,p.IsEnabled,p.RefreshMinutes,p.PlatformsJson,p.NextRunAt,p.LastRunAt,
                       p.LastRunStatus,p.LastErrorCode
                FROM dbo.ContentTopics t
                LEFT JOIN dbo.DiscoveryTopicProfiles p ON p.ContentTopicId=t.Id
                WHERE t.Id=@topicId AND t.TenantId=@tenantId;
                """;
            command.Parameters.AddWithValue("@topicId", topicId);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return Results.NotFound();
            if (reader.IsDBNull(0))
                return Results.Ok(new TopicScheduleResponse(topicId, false, 360, ["TikTokVietnam"], null, null, null, null));
            return Results.Ok(new TopicScheduleResponse(topicId, reader.GetBoolean(1), reader.GetInt32(2),
                JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? ["TikTokVietnam"],
                reader.IsDBNull(4) ? null : reader.GetDateTimeOffset(4),
                reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        });

        group.MapPut("/{topicId:guid}/schedule", async (
            Guid topicId, UpdateTopicScheduleRequest request, ITenantContext tenant,
            IConfiguration configuration, CancellationToken ct) =>
        {
            if (!TryGetTenant(tenant, out var tenantId)) return Results.Forbid();
            if (!AllowedIntervals.Contains(request.RefreshMinutes.ToString()))
                return Results.ValidationProblem(new Dictionary<string, string[]> {
                    ["refreshMinutes"] = ["Chọn chu kỳ 1, 3, 6, 12 hoặc 24 giờ."]
                });
            var platforms = (request.Platforms ?? []).Distinct(StringComparer.Ordinal).ToArray();
            if (platforms.Length == 0 || platforms.Any(x => !AllowedPlatforms.Contains(x)))
                return Results.ValidationProblem(new Dictionary<string, string[]> {
                    ["platforms"] = ["Chọn TikTok Việt Nam và/hoặc Douyin."]
                });
            var cs = configuration.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM dbo.ContentTopics WHERE Id=@topicId AND TenantId=@tenantId)
                    SELECT CAST(0 AS bit);
                ELSE
                BEGIN
                    IF EXISTS (SELECT 1 FROM dbo.DiscoveryTopicProfiles WHERE ContentTopicId=@topicId)
                        UPDATE dbo.DiscoveryTopicProfiles
                        SET IsEnabled=@enabled,RefreshMinutes=@refresh,PlatformsJson=@platforms,
                            NextRunAt=CASE WHEN @enabled=1 THEN DATEADD(minute,@refresh,SYSUTCDATETIME()) ELSE NULL END,
                            LeaseUntil=NULL,UpdatedAt=SYSUTCDATETIME()
                        WHERE ContentTopicId=@topicId;
                    ELSE
                        INSERT dbo.DiscoveryTopicProfiles
                            (Id,ContentTopicId,TargetMarket,ProductContext,AudienceContext,IsEnabled,
                             RefreshMinutes,CreatedAt,UpdatedAt,PlatformsJson,NextRunAt)
                        VALUES
                            (NEWID(),@topicId,N'Vietnam',N'',N'',@enabled,@refresh,SYSUTCDATETIME(),
                             SYSUTCDATETIME(),@platforms,
                             CASE WHEN @enabled=1 THEN DATEADD(minute,@refresh,SYSUTCDATETIME()) ELSE NULL END);
                    SELECT CAST(1 AS bit);
                END
                """;
            command.Parameters.AddWithValue("@topicId", topicId);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            command.Parameters.AddWithValue("@enabled", request.Enabled);
            command.Parameters.AddWithValue("@refresh", request.RefreshMinutes);
            command.Parameters.AddWithValue("@platforms", JsonSerializer.Serialize(platforms));
            var found = (bool)(await command.ExecuteScalarAsync(ct) ?? false);
            if (!found) return Results.NotFound();
            return Results.Ok(new { topicId, request.Enabled, request.RefreshMinutes, platforms });
        });

        group.MapPost("/{topicId:guid}/run-now", async (
            Guid topicId, ITenantContext tenant, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!TryGetTenant(tenant, out var tenantId)) return Results.Forbid();
            var cs = configuration.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DECLARE @profileId uniqueidentifier;
                SELECT @profileId=Id FROM dbo.DiscoveryTopicProfiles
                WHERE ContentTopicId=@topicId;
                IF NOT EXISTS (SELECT 1 FROM dbo.ContentTopics WHERE Id=@topicId AND TenantId=@tenantId)
                    SELECT CAST(NULL AS uniqueidentifier);
                ELSE
                BEGIN
                    IF @profileId IS NULL
                    BEGIN
                        SET @profileId=NEWID();
                        INSERT dbo.DiscoveryTopicProfiles
                            (Id,ContentTopicId,TargetMarket,ProductContext,AudienceContext,IsEnabled,
                             RefreshMinutes,CreatedAt,UpdatedAt,PlatformsJson,NextRunAt)
                        VALUES (@profileId,@topicId,N'Vietnam',N'',N'',0,360,SYSUTCDATETIME(),
                                SYSUTCDATETIME(),N'["TikTokVietnam"]',NULL);
                    END;
                    DECLARE @platforms nvarchar(200) =
                        (SELECT PlatformsJson FROM dbo.DiscoveryTopicProfiles WHERE Id=@profileId);
                    INSERT dbo.DiscoveryTopicRunHistory
                        (Id,TopicProfileId,RunKey,TriggerKind,Status,PlatformsJson,StartedAt)
                    VALUES (NEWID(),@profileId,CONVERT(nvarchar(36),NEWID()),N'Manual',N'Queued',
                            @platforms,SYSUTCDATETIME());
                    SELECT @profileId;
                END
                """;
            command.Parameters.AddWithValue("@topicId", topicId);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            var profileId = await command.ExecuteScalarAsync(ct);
            if (profileId is null || profileId is DBNull) { await transaction.RollbackAsync(ct); return Results.NotFound(); }
            await transaction.CommitAsync(ct);
            return Results.Accepted($"/api/v1/topics/{topicId}/runs", new { topicId, status = "Queued" });
        });

        group.MapGet("/{topicId:guid}/runs", async (
            Guid topicId, int? take, ITenantContext tenant, IConfiguration configuration, CancellationToken ct) =>
        {
            if (!TryGetTenant(tenant, out var tenantId)) return Results.Forbid();
            var size = take ?? 20;
            if (size is < 1 or > 100)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Giá trị từ 1 đến 100."] });
            var cs = configuration.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (@take) h.Id,h.TriggerKind,h.Status,h.PlatformsJson,h.StartedAt,h.CompletedAt,
                       h.CandidateCount,h.ChannelCount,h.ErrorCode
                FROM dbo.DiscoveryTopicRunHistory h
                INNER JOIN dbo.DiscoveryTopicProfiles p ON p.Id=h.TopicProfileId
                INNER JOIN dbo.ContentTopics t ON t.Id=p.ContentTopicId
                WHERE t.Id=@topicId AND t.TenantId=@tenantId
                ORDER BY h.StartedAt DESC,h.Id DESC;
                """;
            command.Parameters.AddWithValue("@take", size);
            command.Parameters.AddWithValue("@topicId", topicId);
            command.Parameters.AddWithValue("@tenantId", tenantId);
            var rows = new List<TopicDiscoveryRunResponse>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(new TopicDiscoveryRunResponse(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),
                    JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [],reader.GetDateTimeOffset(4),
                    reader.IsDBNull(5)?null:reader.GetDateTimeOffset(5),reader.GetInt32(6),reader.GetInt32(7),
                    reader.IsDBNull(8)?null:reader.GetString(8)));
            return Results.Ok(rows);
        });

        return app;
    }

    private static bool TryGetTenant(ITenantContext tenant, out Guid tenantId)
    {
        tenantId = tenant.TenantId ?? Guid.Empty;
        return tenant.IsResolved && tenantId != Guid.Empty;
    }
}

public sealed record UpdateTopicScheduleRequest(bool Enabled, int RefreshMinutes, string[]? Platforms);
public sealed record TopicScheduleResponse(Guid TopicId, bool Enabled, int RefreshMinutes, string[] Platforms,
    DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, string? LastRunStatus, string? LastErrorCode);
public sealed record TopicDiscoveryRunResponse(Guid Id, string TriggerKind, string Status, string[] Platforms,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int CandidateCount, int ChannelCount, string? ErrorCode);
