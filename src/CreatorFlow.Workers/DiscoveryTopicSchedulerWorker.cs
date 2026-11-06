using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
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
        string topicKeywords;
        string platformsJson;
        int refreshMinutes;
        string scheduleMode;
        string localTimeText;
        string daysOfWeekJson;
        string timeZoneId;

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
                       inserted.PlatformsJson,inserted.RefreshMinutes,
                       inserted.ScheduleMode,inserted.LocalTime,inserted.DaysOfWeekJson,inserted.TimeZoneId
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
            scheduleMode = reader.GetString(5);
            localTimeText = reader.GetString(6);
            daysOfWeekJson = reader.GetString(7);
            timeZoneId = reader.GetString(8);
        }

        await using (var topic = connection.CreateCommand())
        {
            topic.Transaction = transaction;
            topic.CommandText = "SELECT Name,Keywords FROM dbo.ContentTopics WHERE Id=@TopicId AND TenantId=@TenantId AND IsActive=1;";
            topic.Parameters.AddWithValue("@TopicId", topicId);
            topic.Parameters.AddWithValue("@TenantId", tenantId);
            await using var topicReader = await topic.ExecuteReaderAsync(ct);
            if (!await topicReader.ReadAsync(ct))
                throw new InvalidOperationException("Chủ đề không còn hoạt động trong workspace.");
            topicName = topicReader.GetString(0);
            topicKeywords = topicReader.GetString(1);
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

        var apiKey = config["BraveSearch:ApiKey"];
        var candidateCount = 0;
        var status = "Skipped";
        string? errorCode = null;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            errorCode = "BraveSearchNotConfigured";
        }
        else
        {
            try
            {
                var (discovered, rateLimited) = await SearchVideosAsync(apiKey, topicName, topicKeywords, ct);
                candidateCount = await SaveCandidatesAsync(connection, tenantId, topicId, discovered, ct);
                status = "Completed";
                if (discovered.Count == 0) errorCode = rateLimited ? "BraveSearchRateLimited" : "NoMatchingVideoUrls";
                else if (rateLimited) errorCode = "BraveSearchRateLimited"; // partial results were still saved
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or SqlException))
            {
                status = "Failed";
                errorCode = ex is TaskCanceledException ? "BraveSearchTimeout"
                    : ex is SqlException ? "CandidateSaveFailed" : "BraveSearchFailed";
                logger.LogWarning(ex, "Tìm video theo lịch cho chủ đề {TopicName} thất bại.", topicName);
            }
        }

        await using var finish = connection.CreateCommand();
        finish.CommandText = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            UPDATE dbo.DiscoveryTopicRunHistory
               SET Status=@Status,CompletedAt=SYSUTCDATETIME(),CandidateCount=@CandidateCount,
                   ChannelCount=0,ErrorCode=@ErrorCode
             WHERE Id=@RunId AND TenantId=@TenantId AND Status=N'Running';
            UPDATE dbo.DiscoveryTopicProfiles
               SET LastRunAt=SYSUTCDATETIME(),LastRunStatus=@Status,
                   LastErrorCode=@ErrorCode,LeaseUntil=NULL,
                   NextRunAt=CASE WHEN IsEnabled=1 THEN @NextRunAt ELSE NULL END,
                   UpdatedAt=SYSUTCDATETIME()
             WHERE Id=@ProfileId AND TenantId=@TenantId;
            COMMIT TRANSACTION;
            """;
        var nextRunAt = CalculateNextRun(DateTimeOffset.UtcNow, scheduleMode, refreshMinutes, localTimeText, daysOfWeekJson, timeZoneId);
        finish.Parameters.AddWithValue("@NextRunAt", nextRunAt);
        finish.Parameters.AddWithValue("@Status", status);
        finish.Parameters.AddWithValue("@CandidateCount", candidateCount);
        finish.Parameters.AddWithValue("@ErrorCode", (object?)errorCode ?? DBNull.Value);
        finish.Parameters.AddWithValue("@RunId", runId);
        finish.Parameters.AddWithValue("@ProfileId", profileId);
        finish.Parameters.AddWithValue("@TenantId", tenantId);
        await finish.ExecuteNonQueryAsync(ct);

        logger.LogInformation(
            "Lịch chủ đề {TopicName} ({ProfileId}) có trạng thái {Status}; lưu mới {CandidateCount} video. Kênh: 0 (connector kênh chưa được bật).",
            topicName, profileId, status, candidateCount);
        return true;
    }
    private static async Task<(List<DiscoveredVideo> Videos, bool RateLimited)> SearchVideosAsync(
        string apiKey, string topic, string keywords, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        var terms = keywords.Split(new[] { '\r', '\n', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
        if (terms.Length == 0) terms = [topic];

        var results = new Dictionary<string, DiscoveredVideo>(StringComparer.OrdinalIgnoreCase);
        var rateLimited = false;
        for (var termIndex = 0; termIndex < terms.Length; termIndex++)
        {
            var term = terms[termIndex];
            // Brave's entry plans allow about one request per second; space the queries out.
            if (termIndex > 0) await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);
            var query = $"site:tiktok.com/@ {topic} {term}";
            var url = "https://api.search.brave.com/res/v1/web/search?q=" +
                Uri.EscapeDataString(query) + "&country=VN&search_lang=vi&count=20&safesearch=moderate";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                // Keep what earlier queries found instead of discarding the whole run.
                rateLimited = true;
                break;
            }
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Brave Search HTTP {(int)response.StatusCode}.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!json.RootElement.TryGetProperty("web", out var web) ||
                !web.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var item in items.EnumerateArray())
            {
                var urlValue = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (!TryGetTikTokVideoId(urlValue, out var id)) continue;
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                results.TryAdd(id, new DiscoveredVideo(id, urlValue!, title ?? topic));
            }
        }
        return (results.Values.Take(100).ToList(), rateLimited);
    }

    private static bool TryGetTikTokVideoId(string? value, out string videoId)
    {
        videoId = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase)))
            return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var video = Array.FindIndex(parts, p => p.Equals("video", StringComparison.OrdinalIgnoreCase));
        if (video < 1 || video + 1 >= parts.Length || !parts[video - 1].StartsWith('@')) return false;
        var handle = Uri.UnescapeDataString(parts[video - 1][1..]);
        var id = Uri.UnescapeDataString(parts[video + 1]);
        if (handle.Length is < 1 or > 64 || !handle.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.') ||
            id.Length is < 1 or > 160 || !id.All(char.IsLetterOrDigit)) return false;
        videoId = id;
        return true;
    }

    private static async Task<int> SaveCandidatesAsync(
        SqlConnection connection, Guid tenantId, Guid topicId, IReadOnlyCollection<DiscoveredVideo> videos,
        CancellationToken ct)
    {
        var inserted = 0;
        foreach (var video in videos)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.DouyinVideoCandidates
                    (Id,TenantId,DouyinVideoId,SourceChannelId,ContentTopicId,SourceUrl,Title,
                     DiscoveryMethod,Status,PublishedAt,DiscoveredAt,UpdatedAt)
                SELECT NEWID(),@TenantId,@VideoId,NULL,@TopicId,@SourceUrl,@Title,
                       N'TopicExplore',N'Candidate',NULL,SYSUTCDATETIME(),SYSUTCDATETIME()
                 WHERE NOT EXISTS
                    (SELECT 1 FROM dbo.DouyinVideoCandidates WITH (UPDLOCK,HOLDLOCK)
                      WHERE TenantId=@TenantId AND DouyinVideoId=@VideoId);
                """;
            command.Parameters.AddWithValue("@TenantId", tenantId);
            command.Parameters.AddWithValue("@TopicId", topicId);
            command.Parameters.AddWithValue("@VideoId", video.VideoId);
            command.Parameters.AddWithValue("@SourceUrl", video.Url);
            command.Parameters.AddWithValue("@Title", video.Title.Length > 500 ? video.Title[..500] : video.Title);
            inserted += await command.ExecuteNonQueryAsync(ct);
        }
        return inserted;
    }

    private sealed record DiscoveredVideo(string VideoId, string Url, string Title);

    private static DateTimeOffset CalculateNextRun(DateTimeOffset nowUtc, string mode, int refreshMinutes,
        string localTimeText, string daysOfWeekJson, string timeZoneId)
    {
        if (mode == "Interval") return nowUtc.AddMinutes(refreshMinutes);
        if (!TimeOnly.TryParseExact(localTimeText, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localTime))
            throw new InvalidOperationException("Lịch chủ đề có giờ chạy không hợp lệ.");

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) when (timeZoneId == "Asia/Ho_Chi_Minh")
        { zone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }

        var days = JsonSerializer.Deserialize<string[]>(daysOfWeekJson) ?? [];
        var selectedDays = days.Select(x => Enum.Parse<DayOfWeek>(x, true)).ToHashSet();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        for (var offset = 0; offset <= 7; offset++)
        {
            var date = localNow.Date.AddDays(offset);
            if (mode == "Weekly" && !selectedDays.Contains(date.DayOfWeek)) continue;
            var candidate = DateTime.SpecifyKind(date.Add(localTime.ToTimeSpan()), DateTimeKind.Unspecified);
            if (candidate <= localNow || zone.IsInvalidTime(candidate)) continue;
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(candidate, zone), TimeSpan.Zero);
        }
        throw new InvalidOperationException("Không thể tính lịch chạy chủ đề tiếp theo.");
    }

}
