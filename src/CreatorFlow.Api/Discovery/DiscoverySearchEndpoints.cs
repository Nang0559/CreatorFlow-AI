using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using CreatorFlow.Infrastructure.Persistence;
using CreatorFlow.Domain.Discovery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Api.Discovery;

public static class DiscoverySearchEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapDiscoverySearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/discovery/search", async (
            DiscoverySearchRequest request,
            CreatorFlowDbContext? db,
            ITenantContext tenant,
            IDataProtectionProvider protection,
            IConfiguration configuration,
            IHttpClientFactory clients,
            CancellationToken cancellationToken) =>
        {
            var topic = (request.Topic ?? "").Trim();
            var queries = (request.Queries ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();

            if (topic.Length is < 2 or > 120)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["topic"] = ["Chủ đề phải từ 2 đến 120 ký tự."] });
            if (queries.Length == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["queries"] = ["Cần ít nhất một truy vấn do AI lập kế hoạch."] });

            var credentials = await DiscoveryConnectorSettingsEndpoints.ResolveAsync(db, tenant, protection, configuration, cancellationToken);
            var results = new List<DiscoverySearchResult>();
            var providerStates = new List<DiscoveryProviderState>();
            var braveKey = credentials.BraveApiKey;
            if (!string.IsNullOrWhiteSpace(braveKey))
            {
                try
                {
                    var brave = await SearchBraveAsync(clients.CreateClient(), braveKey, topic, queries, cancellationToken);
                    results.AddRange(brave);
                    providerStates.Add(new("Brave Search", "Completed", brave.Count, null));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    providerStates.Add(new("Brave Search", "Failed", 0, SafeError(ex)));
                }
            }
            else
            {
                providerStates.Add(new("Brave Search", "NotConfigured", 0, "Thiếu BraveSearch:ApiKey."));
            }

            var researchKey = credentials.TikTokClientKey;
            var researchSecret = credentials.TikTokClientSecret;
            if (!string.IsNullOrWhiteSpace(researchKey) && !string.IsNullOrWhiteSpace(researchSecret))
            {
                try
                {
                    var research = await SearchTikTokResearchAsync(clients.CreateClient(), researchKey, researchSecret, queries, cancellationToken);
                    results.AddRange(research);
                    providerStates.Add(new("TikTok Research API", "Completed", research.Count, null));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
                {
                    providerStates.Add(new("TikTok Research API", "Failed", 0, SafeError(ex)));
                }
            }
            else
            {
                providerStates.Add(new("TikTok Research API", "NotConfigured", 0, "Chưa có Client Key/Secret hoặc dự án chưa được cấp quyền Research API."));
            }

            // Additional providers are declared in configuration; no provider-specific code is
            // required for compatible GET + JSON search APIs (for example SearXNG).
            var workspaceConnector = db is null ? null : await db.DiscoveryConnectorConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
            var configuredProviders = configuration.GetSection("DiscoverySearch:Providers").Get<ConfiguredSearchProvider[]>() ?? [];
            var workspaceProviders = DiscoveryConnectorSettingsEndpoints.ReadCustomProviders(workspaceConnector, protection);
            foreach (var provider in workspaceProviders.Concat(configuredProviders))
            {
                if (!provider.Enabled || string.IsNullOrWhiteSpace(provider.Name) ||
                    string.IsNullOrWhiteSpace(provider.Endpoint))
                    continue;

                if (!IsAllowedProviderEndpoint(provider.Endpoint))
                {
                    providerStates.Add(new(provider.Name, "Failed", 0, "Endpoint phải là URL HTTP(S) hợp lệ."));
                    continue;
                }

                try
                {
                    using var providerClient = SafeProviderHttpClient.Create(TimeSpan.FromSeconds(Math.Clamp(provider.TimeoutSeconds, 3, 45)));
                    var providerResults = await SearchConfiguredProviderAsync(
                        providerClient, provider, topic, queries, cancellationToken);
                    results.AddRange(providerResults);
                    providerStates.Add(new("Custom: " + provider.Name, "Completed", providerResults.Count, null));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
                {
                    providerStates.Add(new("Custom provider", "Failed", 0,
                        ex is TaskCanceledException ? "Nguồn tìm kiếm hết thời gian chờ." : "Không thể truy cập hoặc phân tích phản hồi của provider."));
                }
            }

            var unique = results
                .Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps &&
                    (uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(x => x.Source == "TikTok Research API")
                .ThenByDescending(x => x.Views ?? 0)
                .Take(100)
                .ToArray();

            var videosOnly = unique.Where(x => x.Kind == "Video").ToArray();
            var savedCandidateCount = 0;
            if (request.ContentTopicId.HasValue && videosOnly.Length > 0)
            {
                if (db is null || tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
                    return Results.Problem("Không thể lưu ứng viên khi workspace chưa kết nối cơ sở dữ liệu.", statusCode: 503);
                var topicExists = await db.ContentTopics.AnyAsync(
                    x => x.Id == request.ContentTopicId.Value && x.IsActive, cancellationToken);
                if (!topicExists)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["contentTopicId"] = ["Chủ đề không tồn tại hoặc không còn hoạt động trong workspace hiện tại."] });

                foreach (var video in videosOnly)
                {
                    if (!TryNormalizeTikTokUrl(video.Url, out _, out _, out var videoId) ||
                        string.IsNullOrWhiteSpace(videoId))
                        continue;
                    var exists = await db.DouyinVideoCandidates.AnyAsync(
                        x => x.DouyinVideoId == videoId, cancellationToken);
                    if (exists) continue;
                    var candidate = DouyinVideoCandidate.Create(
                        videoId, null, video.Url, video.Title, VideoDiscoveryMethod.TopicExplore,
                        video.PublishedAt, DateTimeOffset.UtcNow, request.ContentTopicId);
                    db.DouyinVideoCandidates.Add(candidate);
                    savedCandidateCount++;
                }
                if (savedCandidateCount > 0)
                    await db.SaveChangesAsync(cancellationToken);
            }

            var channels = unique
                .Where(x => !string.IsNullOrWhiteSpace(x.Username))
                .GroupBy(x => x.Username!, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First();
                    return new DiscoveryChannelResult(
                        first.Username!,
                        first.DisplayName ?? first.Username!,
                        $"https://www.tiktok.com/@{Uri.EscapeDataString(first.Username!)}",
                        g.Count(x => x.Kind == "Video"),
                        g.Max(x => x.Views ?? 0),
                        g.Max(x => x.Followers),
                        g.Where(x => x.Kind == "Video").Select(x => x.Url).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray(),
                        g.Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? first.Source : "Multiple providers",
                        g.Select(x => x.RetrievedAt).Max());
                })
                .OrderByDescending(x => x.Followers ?? 0)
                .ThenByDescending(x => x.VideoCount)
                .Take(50)
                .ToArray();

            var overallStatus = providerStates.Any(x => x.Status == "Completed")
                ? "Completed"
                : providerStates.Any(x => x.Status == "Failed") ? "Failed" : "NotConfigured";
            return Results.Ok(new DiscoverySearchResponse(
                topic,
                DateTimeOffset.UtcNow,
                overallStatus,
                channels,
                videosOnly,
                providerStates.ToArray(),
                savedCandidateCount,
                "Kết quả chỉ gồm URL TikTok HTTPS trả về từ nguồn tìm kiếm. Kết quả web là trang được lập chỉ mục, không đảm bảo còn tồn tại hoặc mở được; chỉ số chỉ xuất hiện khi API nguồn trả về."));
        }).RequireAuthorization();

        app.MapPost("/api/v1/discovery/search/provider-test", async (
            CustomProviderTestRequest request,
            CreatorFlowDbContext? db,
            ITenantContext tenant,
            IDataProtectionProvider protection,
            IHttpClientFactory clients,
            CancellationToken cancellationToken) =>
        {
            if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
            var row = await db.DiscoveryConnectorConfigurations.AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId, cancellationToken);
            var provider = DiscoveryConnectorSettingsEndpoints.ReadCustomProviders(row, protection)
                .FirstOrDefault(x => x.Id == request.ProviderId);
            if (provider is null) return Results.NotFound(new { error = "Không tìm thấy provider trong workspace." });
            if (!IsAllowedProviderEndpoint(provider.Endpoint))
                return Results.BadRequest(new { error = "Endpoint phải là URL HTTPS công khai hợp lệ." });
            try
            {
                using var providerClient = SafeProviderHttpClient.Create(TimeSpan.FromSeconds(Math.Clamp(provider.TimeoutSeconds, 3, 45)));
                var found = await SearchConfiguredProviderAsync(
                    providerClient, provider, "Kiểm tra kết nối nguồn", ["creatorflow connector verification"], cancellationToken);
                return Results.Ok(new DiscoveryProviderTestResponse(provider.Id, "Custom provider", "Completed", found.Count, null));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
            {
                return Results.Ok(new DiscoveryProviderTestResponse(provider.Id, "Custom provider", "Failed", 0,
                    ex is TaskCanceledException ? "Nguồn tìm kiếm hết thời gian chờ." : "Không thể truy cập hoặc phân tích phản hồi của provider."));
            }
        }).RequireAuthorization("TenantOwnerOrAdmin");

        return app;
    }

    private static async Task<List<DiscoverySearchResult>> SearchBraveAsync(
        HttpClient client, string apiKey, string topic, string[] queries, CancellationToken ct)
    {
        client.Timeout = TimeSpan.FromSeconds(25);
        var found = new List<DiscoverySearchResult>();
        foreach (var query in queries.Take(6))
        {
            var q = $"site:tiktok.com/@ {topic} {query}";
            var url = QueryHelpers.AddQueryString("https://api.search.brave.com/res/v1/web/search",
                new Dictionary<string, string?> { ["q"] = q, ["country"] = "VN", ["search_lang"] = "vi", ["count"] = "20", ["safesearch"] = "moderate" });
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Add("X-Subscription-Token", apiKey);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Brave Search trả HTTP {(int)response.StatusCode}.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!json.RootElement.TryGetProperty("web", out var web) ||
                !web.TryGetProperty("results", out var items) || items.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in items.EnumerateArray())
            {
                var resultUrl = GetString(item, "url");
                var title = GetString(item, "title");
                if (!TryNormalizeTikTokUrl(resultUrl, out var normalized, out var username, out var videoId))
                    continue;
                var description = GetString(item, "description");
                found.Add(new DiscoverySearchResult(
                    normalized, title ?? username ?? "TikTok", description, username, null, null, null, null,
                    videoId is null ? "ChannelOrPage" : "Video", "Brave Search", DateTimeOffset.UtcNow));
            }
        }
        return found;
    }

    private static async Task<List<DiscoverySearchResult>> SearchTikTokResearchAsync(
        HttpClient client, string clientKey, string clientSecret, string[] queries, CancellationToken ct)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/oauth/token/");
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_key"] = clientKey,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "client_credentials"
        });
        using var tokenResponse = await client.SendAsync(tokenRequest, ct);
        if (!tokenResponse.IsSuccessStatusCode)
            throw new HttpRequestException($"TikTok OAuth trả HTTP {(int)tokenResponse.StatusCode}; kiểm tra thông tin client và quyền Research API.");
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
        var token = GetString(tokenJson.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("TikTok OAuth không trả access_token.");

        var results = new List<DiscoverySearchResult>();
        var end = DateTime.UtcNow.Date;
        var start = end.AddDays(-29);
        foreach (var query in queries.Take(4))
        {
            var body = new
            {
                query = new
                {
                    and = new object[]
                    {
                        new { operation = "EQ", field_name = "region_code", field_values = new[] { "VN" } },
                        new { operation = "EQ", field_name = "keyword", field_values = new[] { query } }
                    }
                },
                max_count = 50,
                start_date = start.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                end_date = end.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                is_random = false
            };
            var requestUrl = "https://open.tiktokapis.com/v2/research/video/query/?fields=id,video_description,create_time,username,region_code,view_count,like_count,comment_count,share_count";
            using var message = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            message.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(message, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"TikTok Research API trả HTTP {(int)response.StatusCode}; có thể dự án chưa được phê duyệt hoặc thiếu quyền.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!json.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("videos", out var videos) || videos.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var video in videos.EnumerateArray())
            {
                var id = GetString(video, "id");
                var username = GetString(video, "username");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(username) ||
                    !IsSafeHandle(username))
                    continue;
                var created = GetInt64(video, "create_time");
                var published = created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(created.Value) : (DateTimeOffset?)null;
                var videoUrl = $"https://www.tiktok.com/@{Uri.EscapeDataString(username)}/video/{Uri.EscapeDataString(id)}";
                results.Add(new DiscoverySearchResult(
                    videoUrl,
                    GetString(video, "video_description") ?? $"TikTok video {id}",
                    GetString(video, "video_description"),
                    username,
                    GetInt64(video, "view_count"),
                    GetInt64(video, "like_count"),
                    GetInt64(video, "comment_count"),
                    GetInt64(video, "share_count"),
                    "Video",
                    "TikTok Research API",
                    DateTimeOffset.UtcNow,
                    published));
            }
        }
        return results;
    }

    private static async Task<List<DiscoverySearchResult>> SearchConfiguredProviderAsync(
        HttpClient client, ConfiguredSearchProvider provider, string topic, string[] queries, CancellationToken ct)
    {
        var found = new List<DiscoverySearchResult>();
        var method = (provider.Method ?? "GET").Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST"))
            throw new InvalidOperationException("Provider chỉ hỗ trợ HTTP GET hoặc POST.");
        var queryParameter = string.IsNullOrWhiteSpace(provider.QueryParameter) ? "q" : provider.QueryParameter;
        var resultsPath = string.IsNullOrWhiteSpace(provider.ResultsPath) ? "results" : provider.ResultsPath;
        var urlPath = string.IsNullOrWhiteSpace(provider.UrlPath) ? "url" : provider.UrlPath;
        var titlePath = string.IsNullOrWhiteSpace(provider.TitlePath) ? "title" : provider.TitlePath;
        var descriptionPath = string.IsNullOrWhiteSpace(provider.DescriptionPath) ? "content" : provider.DescriptionPath;
        if (!IsAllowedProviderEndpoint(provider.Endpoint))
            throw new InvalidOperationException("Endpoint không được phép.");
        if (!string.IsNullOrWhiteSpace(provider.HeadersJson))
        {
            using var headerValidation = JsonDocument.Parse(provider.HeadersJson);
            if (headerValidation.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Headers bổ sung không hợp lệ.");
            var blockedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Host", "Connection", "Content-Length", "Transfer-Encoding", "Proxy-Authorization", "Proxy-Connection", "Cookie", "Set-Cookie", "Upgrade", "TE", "Trailer" };
            if (headerValidation.RootElement.EnumerateObject().Any(x => blockedHeaders.Contains(x.Name)))
                throw new InvalidOperationException("Provider sử dụng header bị cấm.");
        }

        foreach (var query in queries.Take(Math.Clamp(provider.MaxQueries, 1, 6)))
        {
            var searchText = (provider.QueryTemplate ?? "{topic} {query}")
                .Replace("{topic}", topic, StringComparison.OrdinalIgnoreCase)
                .Replace("{query}", query, StringComparison.OrdinalIgnoreCase);
            var requestUrl = method == "GET"
                ? QueryHelpers.AddQueryString(provider.Endpoint,
                    new Dictionary<string, string?> { [queryParameter] = searchText })
                : provider.Endpoint;

            using var message = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, requestUrl);
            if (!string.IsNullOrWhiteSpace(provider.ApiKey) && !string.IsNullOrWhiteSpace(provider.ApiKeyHeader))
            {
                var headerValue = (provider.ApiKeyPrefix ?? "") + provider.ApiKey;
                if (!message.Headers.TryAddWithoutValidation(provider.ApiKeyHeader, headerValue))
                    throw new InvalidOperationException("Không thể thiết lập header xác thực cho provider.");
            }
            if (!string.IsNullOrWhiteSpace(provider.HeadersJson))
            {
                using var headers = JsonDocument.Parse(provider.HeadersJson);
                if (headers.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Headers bổ sung phải là một JSON object.");
                foreach (var header in headers.RootElement.EnumerateObject())
                {
                    if (header.Value.ValueKind != JsonValueKind.String)
                        throw new InvalidOperationException($"Header '{header.Name}' phải có giá trị dạng chuỗi.");
                    if (!message.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString()))
                        throw new InvalidOperationException($"Không thể thiết lập header '{header.Name}'.");
                }
            }
            if (!string.IsNullOrWhiteSpace(provider.Accept))
                message.Headers.Accept.ParseAdd(provider.Accept);

            if (method == "POST")
            {
                var bodyTemplate = provider.RequestBodyTemplate;
                if (string.IsNullOrWhiteSpace(bodyTemplate))
                    throw new InvalidOperationException("POST cần khai báo mẫu JSON body.");
                var body = bodyTemplate
                    .Replace("\"{topic}\"", JsonSerializer.Serialize(topic), StringComparison.OrdinalIgnoreCase)
                    .Replace("\"{query}\"", JsonSerializer.Serialize(searchText), StringComparison.OrdinalIgnoreCase);
                using var validation = JsonDocument.Parse(body);
                var contentType = string.IsNullOrWhiteSpace(provider.ContentType) ? "application/json" : provider.ContentType;
                if (!contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Provider chỉ được gửi Content-Type application/json.");
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
                throw new InvalidOperationException("Provider redirect bị chặn.");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Provider trả về phản hồi không thành công.");

            var responseBytes = await SafeProviderHttpClient.ReadBoundedAsync(response.Content, ct);
            using var responseStream = new MemoryStream(responseBytes, writable: false);
            using var json = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);
            if (!TryGetJsonPath(json.RootElement, resultsPath, out var items) || items.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in items.EnumerateArray())
            {
                var resultUrl = ReadPathString(item, urlPath);
                if (!TryNormalizeTikTokUrl(resultUrl, out var normalized, out var username, out var videoId))
                    continue;
                var title = ReadPathString(item, titlePath);
                var description = ReadPathString(item, descriptionPath);
                found.Add(new DiscoverySearchResult(
                    normalized, title ?? username ?? "TikTok", description, username,
                    null, null, null, null, videoId is null ? "ChannelOrPage" : "Video",
                    "Custom: " + provider.Name, DateTimeOffset.UtcNow));
            }
        }

        return found;
    }

    private static bool IsAllowedProviderEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static string? ReadPathString(JsonElement element, string path) =>
        TryGetJsonPath(element, path, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var child))
                return false;
            value = child;
        }
        return true;
    }

    private static bool TryNormalizeTikTokUrl(string? value, out string normalized, out string? username, out string? videoId)
    {
        normalized = "";
        username = null;
        videoId = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase)))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var handleSegment = segments.FirstOrDefault(x => x.StartsWith('@'));
        if (handleSegment is null) return false;
        var handle = Uri.UnescapeDataString(handleSegment[1..]);
        if (!IsSafeHandle(handle)) return false;
        username = handle;
        var videoIndex = Array.FindIndex(segments, x => x.Equals("video", StringComparison.OrdinalIgnoreCase));
        if (videoIndex >= 0 && videoIndex + 1 < segments.Length)
            videoId = segments[videoIndex + 1].Split('?', '#')[0];
        normalized = videoId is null
            ? $"https://www.tiktok.com/@{Uri.EscapeDataString(handle)}"
            : $"https://www.tiktok.com/@{Uri.EscapeDataString(handle)}/video/{Uri.EscapeDataString(videoId)}";
        return true;
    }

    private static bool IsSafeHandle(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '.' );

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? GetInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.TryGetInt64(out var number) ? number : null;

    private static string SafeError(Exception ex) =>
        ex is TaskCanceledException ? "Nguồn tìm kiếm hết thời gian chờ." : ex.Message;
}

public sealed record DiscoverySearchRequest(string Topic, string[] Queries, Guid? ContentTopicId = null);
public sealed record DiscoverySearchResponse(
    string Topic, DateTimeOffset SearchedAt, string Status,
    DiscoveryChannelResult[] Channels, DiscoverySearchResult[] Videos,
    DiscoveryProviderState[] Providers, int SavedCandidateCount, string Notice);
public sealed record DiscoveryProviderState(string Provider, string Status, int ResultCount, string? Error);
public sealed record DiscoveryChannelResult(
    string Username, string DisplayName, string ProfileUrl, int VideoCount,
    long MaxObservedViews, long? Followers, string[] VideoUrls, string Source, DateTimeOffset RetrievedAt);
public sealed record DiscoverySearchResult(
    string Url, string Title, string? Description, string? Username,
    long? Views, long? Likes, long? Comments, long? Shares,
    string Kind, string Source, DateTimeOffset RetrievedAt, DateTimeOffset? PublishedAt = null, long? Followers = null, string? DisplayName = null);

public sealed class ConfiguredSearchProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string? RequestBodyTemplate { get; set; }
    public string? ContentType { get; set; } = "application/json";
    public string? HeadersJson { get; set; }
    public string QueryParameter { get; set; } = "q";
    public string QueryTemplate { get; set; } = "{topic} {query}";
    public string ResultsPath { get; set; } = "results";
    public string UrlPath { get; set; } = "url";
    public string TitlePath { get; set; } = "title";
    public string DescriptionPath { get; set; } = "content";
    public string? ApiKey { get; set; }
    public string? ApiKeyHeader { get; set; }
    public string? ApiKeyPrefix { get; set; }
    public string? Accept { get; set; } = "application/json";
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxQueries { get; set; } = 4;
}

public sealed record CustomProviderTestRequest(Guid ProviderId);
public sealed record DiscoveryProviderTestResponse(Guid ProviderId, string Provider, string Status, int ResultCount, string? Error);
