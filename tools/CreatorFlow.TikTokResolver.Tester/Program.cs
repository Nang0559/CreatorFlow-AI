using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public class Program
{
    private const int MaxHtmlBytes = 5_000_000;
    private const long MaxMediaBytes = 500L * 1024 * 1024;
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient PageClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };
    private static readonly HttpClient MediaClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://127.0.0.1:8095");
        var app = builder.Build();
        app.MapGet("/", () => Results.Content(Page, "text/html; charset=utf-8"));
        app.MapGet("/health", async (CancellationToken ct) =>
        {
            var ytDlp = FindYtDlp();
            var ffmpeg = FindFfmpeg();
            string? ytDlpVersion = null;
            if (ytDlp is not null)
            {
                try
                {
                    var versionResult = await RunYtDlpAsync(ytDlp, new[] { "--version" }, TimeSpan.FromSeconds(10), ct);
                    if (versionResult.ExitCode == 0) ytDlpVersion = versionResult.StdOut.Trim();
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
            return Results.Ok(new
            {
                status = "ok",
                service = "CreatorFlow.TikTokResolver.Tester",
                ytDlpConfigured = ytDlp is not null,
                ytDlpLauncher = ytDlp is null ? "not-found" : Path.GetFileName(ytDlp),
                ytDlpVersion,
                ytDlpUpdateHint = "py -m pip install --upgrade yt-dlp (TikTok đổi thường xuyên; bản cũ là nguyên nhân lỗi phổ biến nhất)",
                ffmpegConfigured = ffmpeg is not null,
                ffmpegPath = ffmpeg
            });
        });
        app.MapGet("/api/resolve", ResolveAsync);
        app.MapGet("/download", DownloadAsync);
        await app.RunAsync();
    }

    private static async Task<IResult> ResolveAsync(string? url, CancellationToken ct)
    {
        if (!TryTikTokUrl(url, out var page, out var validationError))
            return Results.BadRequest(new { success = false, error = validationError });

        var diagnostics = new Dictionary<string, object?>
        {
            ["inputHost"] = page!.Host,
            ["htmlAttempted"] = false,
            ["htmlBytes"] = 0,
            ["hasUniversalData"] = false,
            ["hasSigiState"] = false,
            ["hasOgVideo"] = false,
            ["ytDlpAvailable"] = FindYtDlp() is not null,
            ["ytDlpAttempted"] = false,
            ["ytDlpSucceeded"] = false
        };

        try
        {
            string? title = null, description = null, thumbnail = null, mediaUrl = null, method = null;
            var ytDlpCanDownload = false;
            try
            {
                diagnostics["htmlAttempted"] = true;
                using var response = await GetTikTokPageAsync(page, ct);
                diagnostics["finalPageUrl"] = response.RequestMessage?.RequestUri?.ToString() ?? page.ToString();
                diagnostics["httpStatus"] = (int)response.StatusCode;
                if (response.IsSuccessStatusCode &&
                    response.Content.Headers.ContentLength is not > MaxHtmlBytes)
                {
                    var mediaType = response.Content.Headers.ContentType?.MediaType;
                    if (mediaType is null or "text/html" or "application/xhtml+xml" or "text/plain")
                    {
                        var html = await ReadLimitedTextAsync(response.Content, MaxHtmlBytes, ct);
                        diagnostics["htmlBytes"] = Encoding.UTF8.GetByteCount(html);
                        diagnostics["hasUniversalData"] = html.Contains("__UNIVERSAL_DATA_FOR_REHYDRATION__", StringComparison.Ordinal);
                        diagnostics["hasSigiState"] = html.Contains("SIGI_STATE", StringComparison.Ordinal);
                        var embedded = ExtractEmbeddedVideoData(html);
                        title = embedded.Title ?? ExtractMeta(html, "og:title");
                        description = embedded.Description ?? ExtractMeta(html, "og:description");
                        thumbnail = embedded.CoverUrl ?? ExtractMeta(html, "og:image");
                        mediaUrl = embedded.MediaUrl ?? ExtractMeta(html, "og:video:secure_url")
                            ?? ExtractMeta(html, "og:video") ?? ExtractMeta(html, "twitter:player:stream");
                        diagnostics["hasOgVideo"] = ExtractMeta(html, "og:video") is not null;
                        diagnostics["embeddedDataFound"] = embedded.Found;
                        if (mediaUrl is not null) method = embedded.MediaUrl is not null ? "TikTok embedded JSON" : "OpenGraph metadata";
                    }
                    else diagnostics["htmlContentType"] = mediaType;
                }
                else diagnostics["htmlRejected"] = response.IsSuccessStatusCode ? "HTML exceeds 5 MB" : "TikTok returned non-success status";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics["htmlError"] = SafeError(ex);
            }

            if (!IsAllowedMediaUrl(mediaUrl))
            {
                diagnostics["ytDlpAttempted"] = FindYtDlp() is not null;
                var extracted = await TryYtDlpMetadataAsync(page!, ct);
                diagnostics["ytDlpSucceeded"] = extracted.Success;
                if (extracted.Error is not null) diagnostics["ytDlpError"] = extracted.Error;
                diagnostics["ytDlpInstallHint"] = "py -m pip install --upgrade yt-dlp";
                if (extracted.Success)
                {
                    mediaUrl = extracted.MediaUrl;
                    ytDlpCanDownload = true;
                    title ??= extracted.Title;
                    description ??= extracted.Description;
                    thumbnail ??= extracted.Thumbnail;
                    method = "yt-dlp public TikTok extractor";
                }
            }

            if (!TryAllowedMediaUri(mediaUrl, out var mediaUri) && !ytDlpCanDownload)
            {
                return Results.UnprocessableEntity(new
                {
                    success = false,
                    error = "Chưa tìm được URL media công khai có thể tải. TikTok có thể trả challenge, dữ liệu rút gọn hoặc video không cho phép truy cập trực tiếp. Công cụ không đăng nhập, giải CAPTCHA hay vượt cơ chế bảo vệ.",
                    diagnostics
                });
            }

            return Results.Ok(new
            {
                success = true,
                resolvedPage = page!.ToString(),
                title = title ?? "TikTok public video",
                description = description ?? "",
                thumbnailUrl = SafeThumbnail(thumbnail),
                mediaUrl = mediaUri?.ToString(),
                downloadUrl = "/download?url=" + Uri.EscapeDataString(page.ToString()),
                directDownloadUrl = mediaUri is null ? null : "/download?mediaUrl=" + Uri.EscapeDataString(mediaUri.ToString()),
                contentType = mediaUri is null ? null : "video/mp4",
                extractionMethod = method ?? "Public media URL",
                diagnostics,
                note = "Đã tìm thấy URL media. Chỉ xác nhận tải thành công sau khi kiểm tra tệp MP4 thực tế."
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            diagnostics["fatalError"] = SafeError(ex);
            return Results.Problem("Không thể phân giải video TikTok công khai lúc này.", statusCode: 502,
                extensions: new Dictionary<string, object?> { ["diagnostics"] = diagnostics });
        }
    }

    private static async Task<IResult> DownloadAsync(HttpContext context, string? url, string? mediaUrl, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!TryTikTokUrl(url, out var page, out var error))
                return Results.BadRequest(new { success = false, error });
            var executable = FindYtDlp();
            if (executable is null)
                return Results.Problem("Không tìm thấy yt-dlp/Python. Cài bằng: py -m pip install --upgrade yt-dlp; sau đó khởi động lại tester.", statusCode: 503);

            var tempDir = Path.Combine(Path.GetTempPath(), "CreatorFlowTikTok", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var output = Path.Combine(tempDir, "video.%(ext)s");
                var ffmpeg = FindFfmpeg();
                var infoResult = await RunYtDlpAsync(executable, new[]
                {
                    "--dump-single-json", "--no-warnings", "--no-playlist", "--no-download",
                    "--no-cookies", "--no-cache-dir", "--socket-timeout", "20",
                    "--retries", "1", "--extractor-retries", "1", page!.ToString()
                }, ResolveTimeout, ct);
                if (infoResult.ExitCode != 0)
                    return Results.Problem("Không đọc được danh sách định dạng từ yt-dlp: " + Clip(infoResult.StdErr, 900), statusCode: 502);

                using var infoDocument = JsonDocument.Parse(infoResult.StdOut);
                var infoRoot = infoDocument.RootElement;
                if (YtDlpFormatPlanner.IsImagePlaylist(infoRoot))
                    return Results.UnprocessableEntity(new { error = "Đây là bài đăng ảnh/slideshow, không có luồng video MP4 để tải." });

                var formats = YtDlpFormatPlanner.ParseFormats(infoRoot);
                var plan = YtDlpFormatPlanner.Plan(formats, ffmpeg is not null);
                if (!plan.Success)
                {
                    var status = plan.ErrorCode == YtDlpFormatPlanner.FfmpegRequired ? 503 : 422;
                    return Results.Problem(plan.Error ?? "Không tìm thấy luồng video phù hợp.", statusCode: status,
                        extensions: new Dictionary<string, object?> { ["errorCode"] = plan.ErrorCode, ["formats"] = plan.Formats });
                }
                if (plan.NeedsFfmpeg && ffmpeg is null)
                    return Results.Problem("Định dạng video này cần FFmpeg để ghép hoặc chuyển container MP4.", statusCode: 503);

                var downloadArguments = new List<string>
                {
                    "--no-warnings", "--no-progress", "--no-playlist", "--no-cookies", "--no-cache-dir",
                    "--socket-timeout", "20", "--retries", "1", "--extractor-retries", "1",
                    "--max-filesize", "500M", "--format", plan.FormatSelector!,
                    "--merge-output-format", "mp4", "--output", output
                };
                if (ffmpeg is not null)
                {
                    downloadArguments.Add("--ffmpeg-location");
                    downloadArguments.Add(Path.GetDirectoryName(ffmpeg)!);
                    if (plan.NeedsFfmpeg)
                    {
                        downloadArguments.Add("--remux-video");
                        downloadArguments.Add("mp4");
                    }
                }
                downloadArguments.Add(page!.ToString());
                var result = await RunYtDlpAsync(executable, downloadArguments, DownloadTimeout, ct);
                if (result.ExitCode != 0)
                    return Results.Problem("yt-dlp không tải được video công khai: " + Clip(result.StdErr, 700), statusCode: 502);

                var file = Directory.GetFiles(tempDir, "*.mp4", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f => new FileInfo(f).Length > 0);
                if (file is null)
                {
                    var produced = Directory.GetFiles(tempDir).Select(Path.GetFileName).ToArray();
                    var ffmpegHint = ffmpeg is null
                        ? " Chưa tìm thấy FFmpeg; hãy cài FFmpeg hoặc đặt CREATORFLOW_FFMPEG_PATH."
                        : "";
                    return Results.Problem("yt-dlp không tạo được MP4 đã ghép/remux. Tệp đầu ra: " +
                        string.Join(", ", produced) + "." + ffmpegHint, statusCode: 502);
                }
                var info = new FileInfo(file);
                if (info.Length > MaxMediaBytes) return Results.BadRequest(new { error = "Video vượt giới hạn 500 MB." });
                if (!await HasMp4SignatureAsync(file, ct))
                    return Results.BadRequest(new { error = "FFmpeg/yt-dlp tạo ra tệp không có chữ ký MP4 hợp lệ; từ chối trả tệp." });
                var ffprobe = FindFfprobe(ffmpeg);
                if (ffprobe is not null)
                {
                    var probe = await RunFfprobeAsync(ffprobe, file, ct);
                    if (!probe.Success)
                        return Results.BadRequest(new { error = "Tệp MP4 không có video stream hợp lệ; từ chối trả tệp.", ffprobe = Clip(probe.Error, 700) });
                }
                else
                {
                    return Results.Problem("Không tìm thấy ffprobe.exe để xác minh video stream. Hãy cài bộ FFmpeg đầy đủ (gồm ffprobe) hoặc đặt CREATORFLOW_FFMPEG_PATH.", statusCode: 503);
                }

                context.Response.ContentType = "video/mp4";
                context.Response.Headers.ContentDisposition = "attachment; filename=\"creatorflow-tiktok-video.mp4\"";
                context.Response.ContentLength = info.Length;
                await using var input = File.OpenRead(file);
                await input.CopyToAsync(context.Response.Body, ct);
                return Results.Empty;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return Results.Problem("Lỗi tải video: " + SafeError(ex), statusCode: 502);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        if (!TryAllowedMediaUri(mediaUrl, out var uri))
            return Results.BadRequest(new { error = "Cần cung cấp link TikTok hợp lệ hoặc media URL HTTPS thuộc CDN được cho phép." });
        try
        {
            using var response = await GetMediaAsync(uri!, ct);
            if (!response.IsSuccessStatusCode)
                return Results.Problem("CDN từ chối tải media.", statusCode: (int)response.StatusCode);
            if (response.Content.Headers.ContentLength is > MaxMediaBytes)
                return Results.BadRequest(new { error = "Video vượt giới hạn 500 MB." });
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("video/mp4" or "application/octet-stream"))
                return Results.BadRequest(new { error = "CDN không trả về định dạng video MP4." });

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            var prefix = new byte[64];
            var prefixRead = await source.ReadAsync(prefix.AsMemory(0, prefix.Length), ct);
            if (!ContainsFtyp(prefix, prefixRead))
                return Results.BadRequest(new { error = "Dữ liệu CDN không có chữ ký MP4 (ftyp)." });
            context.Response.ContentType = "video/mp4";
            context.Response.Headers.ContentDisposition = "attachment; filename=\"creatorflow-tiktok-video.mp4\"";
            if (response.Content.Headers.ContentLength.HasValue)
                context.Response.ContentLength = response.Content.Headers.ContentLength.Value;
            await context.Response.Body.WriteAsync(prefix.AsMemory(0, prefixRead), ct);
            var buffer = new byte[64 * 1024];
            long total = prefixRead;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxMediaBytes) { context.Abort(); break; }
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            return Results.Empty;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return Results.Problem("Không thể tải media từ CDN công khai: " + SafeError(ex), statusCode: 502); }
    }

    private static async Task<(bool Success, string? MediaUrl, string? Title, string? Description, string? Thumbnail, string? Error)>
        TryYtDlpMetadataAsync(Uri page, CancellationToken ct)
    {
        var executable = FindYtDlp();
        if (executable is null) return (false, null, null, null, null, "Không tìm thấy yt-dlp. Nếu đã cài bằng pip, hãy kiểm tra Python launcher; cài bằng: py -m pip install --upgrade yt-dlp.");
        try
        {
            var result = await RunYtDlpAsync(executable, new[]
            {
                "--dump-single-json", "--no-warnings", "--no-playlist", "--no-download",
                "--no-cookies", "--no-cache-dir", "--socket-timeout", "15",
                "--retries", "1", "--extractor-retries", "1", page.ToString()
            }, ResolveTimeout, ct);
            if (result.ExitCode != 0)
                return (false, null, null, null, null, Clip(result.StdErr, 900));
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            if (YtDlpFormatPlanner.IsImagePlaylist(root))
                return (false, null, ReadString(root, "title"), ReadString(root, "description"), ReadString(root, "thumbnail"),
                    "Bài đăng TikTok dạng ảnh/slideshow, không có luồng video.");
            var formats = YtDlpFormatPlanner.ParseFormats(root);
            var plan = YtDlpFormatPlanner.Plan(formats, FindFfmpeg() is not null);
            if (!plan.Success)
                return (false, null, ReadString(root, "title"), ReadString(root, "description"), ReadString(root, "thumbnail"),
                    plan.Error ?? "Không tìm thấy luồng video.");
            var media = plan.PreviewUrl;
            if (!IsAllowedMediaUrl(media)) media = null;
            return (true, media, ReadString(root, "title"), ReadString(root, "description"), ReadString(root, "thumbnail"), plan.Warning);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { return (false, null, null, null, null, SafeError(ex)); }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunYtDlpAsync(
        string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var launcherName = Path.GetFileName(executable);
        if (launcherName.Equals("py.exe", StringComparison.OrdinalIgnoreCase) ||
            launcherName.Equals("python.exe", StringComparison.OrdinalIgnoreCase) ||
            launcherName.Equals("python3.exe", StringComparison.OrdinalIgnoreCase) ||
            launcherName.Equals("python", StringComparison.OrdinalIgnoreCase) ||
            launcherName.Equals("python3", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add("-m");
            start.ArgumentList.Add("yt_dlp");
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Không khởi chạy được yt-dlp.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static string? FindFfmpeg()
    {
        var configured = Environment.GetEnvironmentVariable("CREATORFLOW_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured)) return configured;
            var configuredExe = Path.Combine(configured, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            if (File.Exists(configuredExe)) return configuredExe;
        }

        var executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim().Trim('"'), executableName);
            if (File.Exists(candidate)) return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var candidate in new[] { @"C:\Tools\ffmpeg\bin\ffmpeg.exe", @"C:\ffmpeg\bin\ffmpeg.exe" })
                if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? FindFfprobe(string? ffmpeg)
    {
        if (string.IsNullOrWhiteSpace(ffmpeg)) return null;
        var directory = Path.GetDirectoryName(ffmpeg);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static async Task<(bool Success, string? Error)> RunFfprobeAsync(string executable, string file, CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable, UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_type",
                     "-of", "default=noprint_wrappers=1:nokey=1", file })
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) return (false, "Không khởi chạy được ffprobe.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var error = await stderr;
            return (process.ExitCode == 0 && output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(x => x.Equals("video", StringComparison.OrdinalIgnoreCase)), error);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    private static string? FindYtDlp()
    {
        var configured = Environment.GetEnvironmentVariable("CREATORFLOW_YTDLP_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var names = OperatingSystem.IsWindows()
            ? new[] { "yt-dlp.exe", "yt-dlp", "py.exe", "python.exe", "python3.exe" }
            : new[] { "yt-dlp", "python3", "python" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    private static async Task<HttpResponseMessage> GetTikTokPageAsync(Uri page, CancellationToken ct)
    {
        for (var hop = 0; hop < 7; hop++)
        {
            if (!AllowedPageHost(page)) throw new InvalidOperationException("Redirect host không thuộc TikTok allowlist.");
            using var request = new HttpRequestMessage(HttpMethod.Get, page);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            var response = await PageClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidOperationException("Redirect thiếu Location.");
                page = location.IsAbsoluteUri ? location : new Uri(page, location);
                continue;
            }
            return response;
        }
        throw new InvalidOperationException("TikTok redirect vượt quá giới hạn.");
    }

    private static async Task<HttpResponseMessage> GetMediaAsync(Uri uri, CancellationToken ct)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            if (!AllowedMediaHost(uri)) throw new InvalidOperationException("CDN redirect host không được cho phép.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 CreatorFlow TikTok Resolver Tester");
            request.Headers.Referrer = new Uri("https://www.tiktok.com/");
            var response = await MediaClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidOperationException("CDN redirect thiếu Location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            return response;
        }
        throw new InvalidOperationException("CDN redirect vượt quá giới hạn.");
    }

    private static async Task<string> ReadLimitedTextAsync(HttpContent content, int limit, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (memory.Length + read > limit) throw new InvalidOperationException("HTML vượt giới hạn 5 MB.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static (string? MediaUrl, string? Title, string? Description, string? CoverUrl, bool Found) ExtractEmbeddedVideoData(string html)
    {
        foreach (var id in new[] { "__UNIVERSAL_DATA_FOR_REHYDRATION__", "SIGI_STATE", "sigi-persisted-data" })
        {
            var pattern = @"<script\b(?=[^>]*\bid\s*=\s*['""]" + Regex.Escape(id) + @"['""])[^>]*>(.*?)</script>";
            foreach (Match match in Regex.Matches(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                try
                {
                    using var document = JsonDocument.Parse(match.Groups[1].Value);
                    var result = FindVideoFields(document.RootElement);
                    if (result.MediaUrl is not null || result.Title is not null || result.Description is not null)
                        return (result.MediaUrl, result.Title, result.Description, result.CoverUrl, true);
                }
                catch (JsonException) { }
            }
        }
        return (null, null, null, null, false);
    }

    private static (string? MediaUrl, string? Title, string? Description, string? CoverUrl) FindVideoFields(JsonElement root)
    {
        string? media = null, title = null, description = null, cover = null;
        void Visit(JsonElement element, int depth)
        {
            if (depth > 40) return;
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    var name = property.Name;
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            if (media is null && (name.Equals("playAddr", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("downloadAddr", StringComparison.OrdinalIgnoreCase) || name.Equals("play_url", StringComparison.OrdinalIgnoreCase)))
                                media = value.Replace(@"\u002F", "/").Replace(@"\/", "/");
                            else if (title is null && (name.Equals("desc", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("title", StringComparison.OrdinalIgnoreCase)) && value.Length < 1000) title = value;
                            else if (description is null && name.Equals("description", StringComparison.OrdinalIgnoreCase) && value.Length < 3000) description = value;
                            else if (cover is null && (name.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("originCover", StringComparison.OrdinalIgnoreCase) || name.Equals("dynamicCover", StringComparison.OrdinalIgnoreCase))) cover = value;
                        }
                    }
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) Visit(property.Value, depth + 1);
                }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var child in element.EnumerateArray()) Visit(child, depth + 1);
        }
        Visit(root, 0);
        return (media, title, description, cover);
    }

    private static string? ExtractMeta(string html, string key)
    {
        foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var name = Regex.Match(tag.Value, @"(?:property|name)\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (!name.Success || !name.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var content = Regex.Match(tag.Value, @"content\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
            if (content.Success) return WebUtility.HtmlDecode(content.Groups[1].Value);
        }
        return null;
    }

    private static bool TryTikTokUrl(string? value, out Uri? uri, out string error)
    {
        uri = null;
        error = "URL không hợp lệ. Chỉ chấp nhận link HTTPS thuộc TikTok.";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) || !AllowedPageHost(parsed)) return false;
        uri = parsed;
        error = "";
        return true;
    }

    private static bool TryAllowedMediaUri(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var parsed) ||
            !AllowedMediaHost(parsed)) return false;
        uri = parsed;
        return true;
    }

    private static bool IsAllowedMediaUrl(string? value) => TryAllowedMediaUri(value, out _);
    private static bool AllowedPageHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && (HostIs(uri.Host, "tiktok.com") || HostIs(uri.Host, "tiktokv.com"));
    private static bool AllowedMediaHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && (HostIs(uri.Host, "tiktokcdn.com") || HostIs(uri.Host, "tiktokcdn-us.com") ||
        HostIs(uri.Host, "tiktokcdn-eu.com") || HostIs(uri.Host, "tiktok.com") || HostIs(uri.Host, "tiktokv.com") ||
        HostIs(uri.Host, "byteoversea.com") || HostIs(uri.Host, "ibytedtos.com") || HostIs(uri.Host, "muscdn.com"));
    private static bool HostIs(string host, string suffix) => host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);

    private static string? SafeThumbnail(string? value) => TryAllowedMediaUri(value, out var uri) ? uri!.ToString() : null;
    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string SafeError(Exception ex) => Clip(ex.GetType().Name + ": " + ex.Message, 300);
    private static string Clip(string? value, int max) => string.IsNullOrWhiteSpace(value) ? "Không có chi tiết lỗi." :
        value.Length <= max ? value : value[..max];

    private static async Task<bool> HasMp4SignatureAsync(string path, CancellationToken ct)
    {
        var bytes = new byte[64];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAsync(bytes, ct);
        return ContainsFtyp(bytes, read);
    }
    private static bool ContainsFtyp(byte[] bytes, int length) =>
        length >= 12 && bytes.AsSpan(4, Math.Min(length - 4, 32)).IndexOf("ftyp"u8) >= 0;

    private const string Page = """
<!doctype html>
<html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>CreatorFlow TikTok Resolver Tester</title>
<style>
:root{color-scheme:dark}body{font:16px system-ui;background:#101827;color:#f8fafc;margin:0;padding:24px}
main{max-width:760px;margin:auto}h1{font-size:26px}.panel{background:#1e293b;border:1px solid #334155;border-radius:16px;padding:20px;margin:16px 0}
input{box-sizing:border-box;width:100%;padding:14px;border-radius:10px;border:1px solid #64748b;background:#0f172a;color:white;font-size:15px}
button,.download{display:inline-block;margin-top:12px;padding:13px 18px;border:0;border-radius:10px;background:#e87924;color:white;font-weight:700;text-decoration:none;cursor:pointer}
button:disabled{opacity:.5}.muted{color:#cbd5e1}.error{color:#fca5a5}.ok{color:#86efac}img{max-width:220px;border-radius:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere;color:#cbd5e1}
</style></head><body><main><h1>CreatorFlow · TikTok Resolver Tester</h1>
<p class="muted">Bộ thử nghiệm độc lập: metadata công khai + yt-dlp extractor. Không đăng nhập, giải CAPTCHA, vượt DRM hoặc cơ chế kiểm soát truy cập.</p>
<section class="panel"><label for="url">Dán link TikTok</label><input id="url" value="https://vt.tiktok.com/ZSpDTFGSf/" placeholder="https://www.tiktok.com/...">
<button id="resolve">Phân giải video</button><p id="status" class="muted">Sẵn sàng kiểm tra.</p></section>
<section id="result" class="panel" hidden><h2 id="title"></h2><img id="thumb" hidden alt="Thumbnail"><p id="desc" class="muted"></p><a id="download" class="download" hidden>Tải MP4 về máy tính</a> <a id="direct" class="download" hidden>Tải trực tiếp từ CDN (dự phòng)</a><details open><summary>Thông tin kỹ thuật / chẩn đoán</summary><pre id="debug"></pre></details></section>
<script>
const $=id=>document.getElementById(id);
$("resolve").addEventListener("click",async()=>{
 const url=$("url").value.trim(); $("status").textContent="Đang kiểm tra metadata và yt-dlp…";$("status").className="muted";$("result").hidden=true;$("resolve").disabled=true;
 try{
  const r=await fetch("/api/resolve?url="+encodeURIComponent(url));const d=await r.json();
  $("debug").textContent=JSON.stringify(d,null,2);$("result").hidden=false;
  if(!r.ok||!d.success){$("title").textContent="Chưa phân giải được video";$("desc").textContent="Xem phần chẩn đoán bên dưới để biết tầng nào thất bại.";$("thumb").hidden=true;$("download").hidden=true;$("direct").hidden=true;$("status").textContent=d.error||d.detail||"Không phân giải được video.";$("status").className="error";return;}
  $("title").textContent=d.title||"TikTok video";$("desc").textContent=d.description||"";
  if(d.thumbnailUrl){$("thumb").src=d.thumbnailUrl;$("thumb").hidden=false}else $("thumb").hidden=true;
  $("download").href=d.downloadUrl;$("download").hidden=false;
  if(d.directDownloadUrl){$("direct").href=d.directDownloadUrl;$("direct").hidden=false}else $("direct").hidden=true;
  $("status").textContent="Đã tìm thấy media URL. Nút tải sẽ dùng yt-dlp để tải và kiểm tra MP4."; $("status").className="ok";
 }catch(e){$("result").hidden=false;$("title").textContent="Lỗi kết nối";$("debug").textContent=String(e);$("status").textContent=e.message;$("status").className="error"}
 finally{$("resolve").disabled=false}
});
</script></main></body></html>
""";
}