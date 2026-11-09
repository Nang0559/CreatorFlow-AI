using System.Net;
using System.Text.RegularExpressions;

namespace CreatorFlow.Api.Video;

public static class PublicTikTokResolverEndpoints
{
    private const int MaxHtmlBytes = 5_000_000;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public static IEndpointRouteBuilder MapPublicTikTokResolver(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/public-media/tiktok/resolve", ResolveAsync)
            .AllowAnonymous()
            .RequireRateLimiting("public-tiktok-resolver");
        return endpoints;
    }

    private static async Task<IResult> ResolveAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out var page) || !AllowedPageHost(page))
            return Results.BadRequest(new { error = "Liên kết không hợp lệ hoặc không thuộc TikTok công khai được hỗ trợ." });

        try
        {
            HttpResponseMessage? response = null;
            for (var hop = 0; hop < 6; hop++)
            {
                if (!AllowedPageHost(page))
                    return Results.BadRequest(new { error = "TikTok chuyển hướng ra ngoài miền được hỗ trợ; CreatorFlow đã chặn." });
                using var request = new HttpRequestMessage(HttpMethod.Get, page);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1");
                response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    response.Dispose();
                    if (location is null) return Results.BadRequest(new { error = "Liên kết chuyển hướng không hợp lệ." });
                    page = location.IsAbsoluteUri ? location : new Uri(page, location);
                    continue;
                }
                break;
            }

            if (response is null) return Results.BadRequest(new { error = "TikTok chuyển hướng quá nhiều lần." });
            using (response)
            {
                if (!response.IsSuccessStatusCode)
                    return Results.Problem("TikTok không cho phép truy cập trang video công khai này.", statusCode: (int)response.StatusCode);
                if (response.Content.Headers.ContentLength is > MaxHtmlBytes)
                    return Results.BadRequest(new { error = "Trang TikTok vượt giới hạn xử lý 5 MB." });
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null && mediaType is not ("text/html" or "application/xhtml+xml" or "text/plain"))
                    return Results.BadRequest(new { error = "TikTok không trả về trang HTML công khai." });
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                using var memory = new MemoryStream();
                var buffer = new byte[32 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    if (memory.Length + read > MaxHtmlBytes)
                        return Results.BadRequest(new { error = "Trang TikTok vượt giới hạn xử lý 5 MB." });
                    await memory.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                var html = System.Text.Encoding.UTF8.GetString(memory.ToArray());
                var media = ExtractMeta(html, "og:video:secure_url") ?? ExtractMeta(html, "og:video") ?? ExtractMeta(html, "twitter:player:stream");
                if (!Uri.TryCreate(WebUtility.HtmlDecode(media), UriKind.Absolute, out var mediaUri) || !AllowedMediaHost(mediaUri))
                    return Results.UnprocessableEntity(new { error = "Video này không công khai URL MP4 trực tiếp. Hãy thử video công khai khác; CreatorFlow không vượt đăng nhập, CAPTCHA hoặc cơ chế bảo vệ." });
                return Results.Ok(new
                {
                    success = true,
                    title = ExtractMeta(html, "og:title") ?? "TikTok public video",
                    description = ExtractMeta(html, "og:description") ?? "",
                    thumbnailUrl = SafeThumbnail(ExtractMeta(html, "og:image")),
                    mediaUrl = mediaUri.ToString(),
                    contentType = "video/mp4",
                    availableFormats = new[] { new { id = "source", label = "MP4 · nguồn công khai", mediaUrl = mediaUri.ToString() } },
                    downloadMode = "direct-public-cdn",
                    note = "Chỉ có định dạng MP4 được metadata công khai xác nhận; URL có thể hết hạn hoặc không truy cập được trên mọi thiết bị."
                });
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return Results.Problem("Không thể phân giải video TikTok công khai lúc này.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static string? ExtractMeta(string html, string key)
    {
        foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var name = Regex.Match(tag.Value, @"(?:property|name)\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (!name.Success || !name.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var content = Regex.Match(tag.Value, @"content\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (content.Success) return content.Groups[1].Value;
        }
        return null;
    }

    private static string? SafeThumbnail(string? value)
    {
        if (Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var uri) &&
            uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) &&
            (HostIs(uri.Host, "tiktokcdn.com") || HostIs(uri.Host, "tiktok.com") ||
             HostIs(uri.Host, "byteoversea.com") || HostIs(uri.Host, "ibytedtos.com")))
            return uri.ToString();
        return null;
    }

    private static bool AllowedPageHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (HostIs(uri.Host, "tiktok.com") || HostIs(uri.Host, "tiktokv.com"));

    private static bool AllowedMediaHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (HostIs(uri.Host, "tiktokcdn.com") || HostIs(uri.Host, "tiktokcdn-us.com") || HostIs(uri.Host, "tiktokcdn-eu.com") ||
         HostIs(uri.Host, "tiktok.com") || HostIs(uri.Host, "tiktokv.com") || HostIs(uri.Host, "byteoversea.com") || HostIs(uri.Host, "ibytedtos.com") || HostIs(uri.Host, "muscdn.com"));

    private static bool HostIs(string host, string suffix) => host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
}
