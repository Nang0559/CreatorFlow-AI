using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace CreatorFlow.Api.Discovery;

public static class DiscoverySearchEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapDiscoverySearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/discovery/search", async (
            DiscoverySearchRequest request,
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

            var results = new List<DiscoverySearchResult>();
            var providerStates = new List<DiscoveryProviderState>();
            var braveKey = configuration["BraveSearch:ApiKey"];
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

            var researchKey = configuration["TikTokResearch:ClientKey"];
            var researchSecret = configuration["TikTokResearch:ClientSecret"];
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
                        g.Count(),
                        g.Max(x => x.Views ?? 0),
                        g.Max(x => x.Followers),
                        g.Select(x => x.Url).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray(),
                        g.Any(x => x.Source == "TikTok Research API") ? "TikTok Research API" : "Brave Search",
                        g.Select(x => x.VerifiedAt).Max());
                })
                .OrderByDescending(x => x.Followers ?? 0)
                .ThenByDescending(x => x.VideoCount)
                .Take(50)
                .ToArray();

            var configured = providerStates.Any(x => x.Status == "Completed");
            return Results.Ok(new DiscoverySearchResponse(
                topic,
                DateTimeOffset.UtcNow,
                configured ? "Completed" : "NotConfigured",
                channels,
                unique,
                providerStates,
                "Kết quả chỉ gồm URL TikTok HTTPS trả về từ nguồn tìm kiếm. Kết quả web là trang được lập chỉ mục, không đảm bảo còn tồn tại hoặc mở được; chỉ số chỉ xuất hiện khi API nguồn trả về."));
        }).RequireAuthorization();

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

public sealed record DiscoverySearchRequest(string Topic, string[] Queries);
public sealed record DiscoverySearchResponse(
    string Topic, DateTimeOffset SearchedAt, string Status,
    DiscoveryChannelResult[] Channels, DiscoverySearchResult[] Videos,
    DiscoveryProviderState[] Providers, string Notice);
public sealed record DiscoveryProviderState(string Provider, string Status, int ResultCount, string? Error);
public sealed record DiscoveryChannelResult(
    string Username, string DisplayName, string ProfileUrl, int VideoCount,
    long MaxObservedViews, long? Followers, string[] VideoUrls, string Source, DateTimeOffset VerifiedAt);
public sealed record DiscoverySearchResult(
    string Url, string Title, string? Description, string? Username,
    long? Views, long? Likes, long? Comments, long? Shares,
    string Kind, string Source, DateTimeOffset VerifiedAt, DateTimeOffset? PublishedAt = null, long? Followers = null, string? DisplayName = null);
