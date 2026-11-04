using System.Globalization;
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
            if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return Results.Forbid();
            var cs = config.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP(1) p.Id,p.IsEnabled,p.RefreshMinutes,p.PlatformsJson,p.NextRunAt,
                       p.LastRunAt,p.LastRunStatus,p.LastErrorCode,p.LeaseUntil,
                       (SELECT TOP(1) h.CandidateCount FROM dbo.DiscoveryTopicRunHistory h
                         WHERE h.TenantId=p.TenantId AND h.TopicProfileId=p.Id ORDER BY h.StartedAt DESC),
                       (SELECT TOP(1) h.ChannelCount FROM dbo.DiscoveryTopicRunHistory h
                         WHERE h.TenantId=p.TenantId AND h.TopicProfileId=p.Id ORDER BY h.StartedAt DESC),
                       p.ScheduleMode,p.LocalTime,p.DaysOfWeekJson,p.TimeZoneId
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
                reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                reader.GetString(11), reader.GetString(12),
                JsonSerializer.Deserialize<string[]>(reader.GetString(13)) ?? [],
                reader.GetString(14)));
        });

        group.MapPut("/", async (Guid topicId, UpdateTopicScheduleRequest request, ITenantContext tenant,
            IConfiguration config, CancellationToken ct) =>
        {
            if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return Results.Forbid();
            if (request.RefreshMinutes is < 5 or > 1440)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["refreshMinutes"] = ["Chu kỳ phải từ 5 đến 1.440 phút."] });

            var mode = (request.ScheduleMode ?? "Interval").Trim();
            if (mode is not ("Interval" or "Daily" or "Weekly"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["scheduleMode"] = ["Chọn theo chu kỳ, hằng ngày hoặc hằng tuần."] });
            if (!TimeOnly.TryParseExact(request.LocalTime ?? "09:00", "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var localTime))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["localTime"] = ["Giờ chạy phải theo định dạng HH:mm."] });

            var days = (request.DaysOfWeek ?? [])
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (mode == "Weekly" && (days.Length == 0 || days.Any(x => !Enum.TryParse<DayOfWeek>(x, true, out _))))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["daysOfWeek"] = ["Với lịch hằng tuần, hãy chọn ít nhất một ngày hợp lệ."] });

            var timeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? "Asia/Ho_Chi_Minh" : request.TimeZoneId.Trim();
            TimeZoneInfo zone;
            try { zone = FindTimeZone(timeZoneId); timeZoneId = zone.Id; }
            catch (TimeZoneNotFoundException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = ["Múi giờ không được hỗ trợ."] });
            }
            catch (InvalidTimeZoneException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZoneId"] = ["Cấu hình múi giờ không hợp lệ."] });
            }

            var platforms = (request.Platforms ?? ["TikTokVietnam"])
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (platforms.Length == 0 || platforms.Any(x => x is not ("TikTokVietnam" or "YouTubeShortsVietnam" or "FacebookReelsVietnam")))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["platforms"] = ["Nền tảng hỗ trợ: TikTokVietnam, YouTubeShortsVietnam, FacebookReelsVietnam."] });

            var nextRunAt = request.IsEnabled
                ? CalculateNextRun(DateTimeOffset.UtcNow, mode, request.RefreshMinutes, localTime, days, zone)
                : (DateTimeOffset?)null;
            var cs = config.GetConnectionString("CreatorFlow");
            if (string.IsNullOrWhiteSpace(cs)) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var platformsJson = JsonSerializer.Serialize(platforms);
            var daysJson = JsonSerializer.Serialize(days);
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
                           ScheduleMode=@ScheduleMode,LocalTime=@LocalTime,DaysOfWeekJson=@DaysOfWeekJson,TimeZoneId=@TimeZoneId,
                           NextRunAt=@NextRunAt,LeaseUntil=CASE WHEN @IsEnabled=0 THEN NULL ELSE LeaseUntil END,
                           UpdatedAt=SYSUTCDATETIME()
                     WHERE TenantId=@TenantId AND ContentTopicId=@TopicId;
                ELSE
                    INSERT dbo.DiscoveryTopicProfiles
                        (Id,TenantId,ContentTopicId,TargetMarket,ProductContext,AudienceContext,IsEnabled,
                         RefreshMinutes,CreatedAt,UpdatedAt,PlatformsJson,NextRunAt,ScheduleMode,LocalTime,DaysOfWeekJson,TimeZoneId)
                    VALUES
                        (NEWID(),@TenantId,@TopicId,N'Vietnam',N'',N'',@IsEnabled,@RefreshMinutes,
                         SYSUTCDATETIME(),SYSUTCDATETIME(),@PlatformsJson,@NextRunAt,
                         @ScheduleMode,@LocalTime,@DaysOfWeekJson,@TimeZoneId);
                COMMIT TRANSACTION;
                SELECT CAST(1 AS bit) AS TopicFound;
                """;
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@TopicId", topicId);
            command.Parameters.AddWithValue("@IsEnabled", request.IsEnabled);
            command.Parameters.AddWithValue("@RefreshMinutes", request.RefreshMinutes);
            command.Parameters.AddWithValue("@PlatformsJson", platformsJson);
            command.Parameters.AddWithValue("@ScheduleMode", mode);
            command.Parameters.AddWithValue("@LocalTime", localTime.ToString("HH:mm", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@DaysOfWeekJson", daysJson);
            command.Parameters.AddWithValue("@TimeZoneId", timeZoneId);
            command.Parameters.AddWithValue("@NextRunAt", (object?)nextRunAt ?? DBNull.Value);
            var found = (bool?)await command.ExecuteScalarAsync(ct) ?? false;
            if (!found) return Results.NotFound();
            return Results.Ok(new { saved = true, isEnabled = request.IsEnabled, refreshMinutes = request.RefreshMinutes,
                scheduleMode = mode, localTime = localTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                daysOfWeek = days, timeZoneId, nextRunAt, platforms });
        });

        group.MapGet("/history", async (Guid topicId, int? take, ITenantContext tenant,
            IConfiguration config, CancellationToken ct) =>
        {
            if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return Results.Forbid();
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

    internal static TimeZoneInfo FindTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) when (id == "Asia/Ho_Chi_Minh")
        { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }

    internal static DateTimeOffset CalculateNextRun(DateTimeOffset nowUtc, string mode, int refreshMinutes,
        TimeOnly localTime, string[] days, TimeZoneInfo zone)
    {
        if (mode == "Interval") return nowUtc.AddMinutes(refreshMinutes);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        var selectedDays = days.Select(x => Enum.Parse<DayOfWeek>(x, true)).ToHashSet();
        for (var offset = 0; offset <= 7; offset++)
        {
            var date = localNow.Date.AddDays(offset);
            if (mode == "Weekly" && !selectedDays.Contains(date.DayOfWeek)) continue;
            var localCandidate = DateTime.SpecifyKind(date.Add(localTime.ToTimeSpan()), DateTimeKind.Unspecified);
            if (localCandidate <= localNow) continue;
            if (zone.IsInvalidTime(localCandidate)) continue;
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localCandidate, zone), TimeSpan.Zero);
        }
        throw new InvalidOperationException("Không thể tính thời điểm chạy lịch tiếp theo.");
    }
}

public sealed record UpdateTopicScheduleRequest(bool IsEnabled, int RefreshMinutes, string[]? Platforms,
    string? ScheduleMode = "Interval", string? LocalTime = "09:00", string[]? DaysOfWeek = null,
    string? TimeZoneId = "Asia/Ho_Chi_Minh");
public sealed record TopicScheduleResponse(Guid Id, bool IsEnabled, int RefreshMinutes, string PlatformsJson,
    DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, string? LastRunStatus, string? LastErrorCode,
    DateTimeOffset? LeaseUntil, int CandidateCount, int ChannelCount, string ScheduleMode, string LocalTime,
    string[] DaysOfWeek, string TimeZoneId);
public sealed record TopicScheduleRunResponse(Guid Id, string TriggerKind, string Status,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int CandidateCount, int ChannelCount, string? ErrorCode);
