using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://127.0.0.1:8095");
builder.Services.AddProblemDetails();
var app = builder.Build();

app.MapGet("/", () => Results.Content(Page, "text/html; charset=utf-8"));
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "CreatorFlow.TikTokResolver.Tester" }));
app.MapGet("/api/resolve", ResolveAsync);
app.MapGet("/download", DownloadAsync);

app.Run();

static readonly HttpClient PageClient = new(new HttpClientHandler { AllowAutoRedirect = false })
{
    Timeout = TimeSpan.FromSeconds(25)
};
static readonly HttpClient MediaClient = new(new HttpClientHandler { AllowAutoRedirect = false })
{
    Timeout = TimeSpan.FromMinutes(3)
};
const int MaxHtmlBytes = 5_000_000;
const long MaxMediaBytes = 500L * 1024 * 1024;

static async Task<IResult> ResolveAsync(string? url, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 ||
        !Uri.TryCreate(url, UriKind.Absolute, out var page) || !AllowedPageHost(page))
        return Results.BadRequest(new { success = false, error = "URL không hợp lệ hoặc không phải link TikTok được hỗ trợ." });

    try
    {
        using var response = await GetTikTokPageAsync(page, ct);
        if (!response.IsSuccessStatusCode)
            return Results.Problem("TikTok từ chối truy cập trang này.", statusCode: (int)response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaxHtmlBytes)
            return Results.BadRequest(new { success = false, error = "Trang TikTok vượt giới hạn 5 MB." });

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType is not ("text/html" or "application/xhtml+xml" or "text/plain"))
            return Results.BadRequest(new { success = false, error = "TikTok không trả về HTML." });

        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + read > MaxHtmlBytes)
                return Results.BadRequest(new { success = false, error = "Trang TikTok vượt giới hạn 5 MB." });
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        var html = Encoding.UTF8.GetString(memory.ToArray());
        var media = ExtractMeta(html, "og:video:secure_url")
                    ?? ExtractMeta(html, "og:video")
                    ?? ExtractMeta(html, "twitter:player:stream");
        if (!Uri.TryCreate(WebUtility.HtmlDecode(media), UriKind.Absolute, out var mediaUri) || !AllowedMediaHost(mediaUri))
            return Results.UnprocessableEntity(new
            {
                success = false,
                error = "Không tìm thấy URL MP4 công khai trong metadata. Hãy thử video công khai khác. Không vượt đăng nhập, CAPTCHA hoặc cơ chế bảo vệ."
            });

        return Results.Ok(new
        {
            success = true,
            resolvedPage = page.ToString(),
            title = ExtractMeta(html, "og:title") ?? "TikTok public video",
            description = ExtractMeta(html, "og:description") ?? "",
            thumbnailUrl = SafeThumbnail(ExtractMeta(html, "og:image")),
            mediaUrl = mediaUri.ToString(),
            downloadUrl = "/download?mediaUrl=" + Uri.EscapeDataString(mediaUri.ToString()),
            contentType = "video/mp4",
            note = "Đây là URL media được metadata công khai cung cấp; khả năng tải còn phụ thuộc CDN và thời hạn URL."
        });
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception)
    {
        return Results.Problem("Không thể phân giải video TikTok công khai lúc này.", statusCode: 502);
    }
}

static async Task<HttpResponseMessage> GetTikTokPageAsync(Uri page, CancellationToken ct)
{
    for (var hop = 0; hop < 6; hop++)
    {
        if (!AllowedPageHost(page)) throw new InvalidOperationException("Blocked redirect host.");
        using var request = new HttpRequestMessage(HttpMethod.Get, page);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1");
        var response = await PageClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidOperationException("Redirect without location.");
            page = location.IsAbsoluteUri ? location : new Uri(page, location);
            continue;
        }
        return response;
    }
    throw new InvalidOperationException("Too many redirects.");
}

static async Task<IResult> DownloadAsync(HttpContext context, string? mediaUrl, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(mediaUrl) || mediaUrl.Length > 4096 ||
        !Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || !AllowedMediaHost(uri))
        return Results.BadRequest(new { error = "Media URL không hợp lệ hoặc host không được cho phép." });

    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 CreatorFlow TikTok Resolver Tester");
        using var response = await MediaClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Results.Problem("CDN từ chối tải media.", statusCode: (int)response.StatusCode);
        if (response.Content.Headers.ContentLength is > MaxMediaBytes)
            return Results.BadRequest(new { error = "Video vượt giới hạn 500 MB." });
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("video/mp4" or "application/octet-stream"))
            return Results.BadRequest(new { error = "CDN không trả về định dạng video MP4." });

        context.Response.StatusCode = 200;
        context.Response.ContentType = "video/mp4";
        context.Response.Headers.ContentDisposition = "attachment; filename=\"creatorflow-tiktok-video.mp4\"";
        if (response.Content.Headers.ContentLength.HasValue)
            context.Response.ContentLength = response.Content.Headers.ContentLength.Value;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxMediaBytes)
            {
                context.Abort();
                break;
            }
            await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return Results.Empty;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch
    {
        return Results.Problem("Không thể tải media từ CDN công khai.", statusCode: 502);
    }
}

static string? ExtractMeta(string html, string key)
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

static string? SafeThumbnail(string? value) =>
    Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var uri) &&
    uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) &&
    (HostIs(uri.Host, "tiktokcdn.com") || HostIs(uri.Host, "tiktok.com") ||
     HostIs(uri.Host, "byteoversea.com") || HostIs(uri.Host, "ibytedtos.com"))
        ? uri.ToString() : null;

static bool AllowedPageHost(Uri uri) =>
    uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
    (HostIs(uri.Host, "tiktok.com") || HostIs(uri.Host, "tiktokv.com"));

static bool AllowedMediaHost(Uri uri) =>
    uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
    (HostIs(uri.Host, "tiktokcdn.com") || HostIs(uri.Host, "tiktokcdn-us.com") ||
     HostIs(uri.Host, "tiktokcdn-eu.com") || HostIs(uri.Host, "tiktok.com") ||
     HostIs(uri.Host, "tiktokv.com") || HostIs(uri.Host, "byteoversea.com") ||
     HostIs(uri.Host, "ibytedtos.com") || HostIs(uri.Host, "muscdn.com"));

static bool HostIs(string host, string suffix) =>
    host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
    host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);

const string Page = """
<!doctype html>
<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>CreatorFlow TikTok Resolver Tester</title>
<style>
:root{color-scheme:dark}body{font:16px system-ui;background:#101827;color:#f8fafc;margin:0;padding:24px}
main{max-width:760px;margin:auto}h1{font-size:26px}.panel{background:#1e293b;border:1px solid #334155;border-radius:16px;padding:20px;margin:16px 0}
input{box-sizing:border-box;width:100%;padding:14px;border-radius:10px;border:1px solid #64748b;background:#0f172a;color:white;font-size:15px}
button,.download{display:inline-block;margin-top:12px;padding:13px 18px;border:0;border-radius:10px;background:#e87924;color:white;font-weight:700;text-decoration:none;cursor:pointer}
.muted{color:#cbd5e1}.error{color:#fca5a5}.ok{color:#86efac}img{max-width:220px;border-radius:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere;color:#cbd5e1}
</style></head><body><main><h1>CreatorFlow · TikTok Resolver Tester</h1>
<p class="muted">Công cụ thử nghiệm độc lập. Chỉ phân giải media công khai, không vượt đăng nhập/CAPTCHA/DRM.</p>
<section class="panel"><label for="url">Dán link TikTok</label><input id="url" value="https://vt.tiktok.com/ZSpDTFGSf/" placeholder="https://www.tiktok.com/...">
<button id="resolve">Phân giải video</button><p id="status" class="muted">Sẵn sàng kiểm tra.</p></section>
<section id="result" class="panel" hidden><h2 id="title"></h2><img id="thumb" hidden alt="Thumbnail"><p id="desc" class="muted"></p><a id="download" class="download" hidden>Tải MP4 về máy tính</a><details><summary>Thông tin kỹ thuật</summary><pre id="debug"></pre></details></section>
<script>
const $=id=>document.getElementById(id);
$("resolve").addEventListener("click",async()=>{
 const url=$("url").value.trim(); $("status").textContent="Đang kiểm tra link TikTok…";$("status").className="muted";$("result").hidden=true;$("resolve").disabled=true;
 try{
  const r=await fetch("/api/resolve?url="+encodeURIComponent(url));const d=await r.json();
  if(!r.ok||!d.success)throw new Error(d.error||d.detail||"Không phân giải được video.");
  $("title").textContent=d.title||"TikTok video";$("desc").textContent=d.description||"";
  if(d.thumbnailUrl){$("thumb").src=d.thumbnailUrl;$("thumb").hidden=false}else $("thumb").hidden=true;
  $("download").href=d.downloadUrl;$("download").hidden=false;$("debug").textContent=JSON.stringify(d,null,2);
  $("status").textContent="Đã tìm thấy URL media công khai. Hãy thử tải MP4."; $("status").className="ok";$("result").hidden=false;
 }catch(e){$("status").textContent=e.message;$("status").className="error"}
 finally{$("resolve").disabled=false}
});
</script></main></body></html>
""";
