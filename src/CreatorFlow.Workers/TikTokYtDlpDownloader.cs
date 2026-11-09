using System.Diagnostics;
using System.Text.Json;

namespace CreatorFlow.Workers;

internal sealed record TikTokDownloadedMedia(string Title, string FilePath);

/// <summary>
/// Downloads only formats explicitly exposed by yt-dlp for the submitted public URL.
/// It never supplies cookies, credentials, browser profiles, or challenge-solving services.
/// </summary>
internal static class TikTokYtDlpDownloader
{
    private const long MaxBytes = 500L * 1024 * 1024;

    public static async Task<TikTokDownloadedMedia> DownloadAsync(
        string pageUrl, string fallbackTitle, string workDirectory,
        IConfiguration config, CancellationToken ct)
    {
        Directory.CreateDirectory(workDirectory);
        var yt = FindTool(config["VideoRendering:YtDlpPath"], "yt-dlp.exe", "yt-dlp", "py.exe", "python.exe");
        if (yt is null)
            throw new InvalidOperationException("Chưa tìm thấy yt-dlp. Cài bằng 'py -m pip install --upgrade yt-dlp' và cấu hình VideoRendering:YtDlpPath nếu cần.");

        var metadataArgs = new List<string>();
        AddLauncher(metadataArgs, yt);
        metadataArgs.AddRange(["--dump-single-json", "--no-warnings", "--no-playlist", "--no-download",
            "--no-cookies", "--no-cache-dir", "--socket-timeout", "20", "--retries", "1",
            "--extractor-retries", "1", pageUrl]);
        var metadata = await RunAsync(yt, metadataArgs, TimeSpan.FromSeconds(60), ct);
        if (metadata.ExitCode != 0)
            throw new InvalidOperationException("Không đọc được định dạng media công khai từ TikTok: " + Clip(metadata.StdErr, 700));

        using var doc = JsonDocument.Parse(metadata.StdOut);
        var root = doc.RootElement;
        var title = ReadString(root, "title") ?? fallbackTitle;
        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array &&
            !(root.TryGetProperty("formats", out var entryFormats) && entryFormats.ValueKind == JsonValueKind.Array && entryFormats.GetArrayLength() > 0))
            throw new InvalidOperationException("Bài đăng TikTok là ảnh/slideshow, không có luồng video để tải.");

        var formats = new List<Format>();
        if (root.TryGetProperty("formats", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in array.EnumerateArray())
            {
                var id = ReadString(f, "format_id");
                if (string.IsNullOrWhiteSpace(id) || id.Equals("download", StringComparison.OrdinalIgnoreCase)) continue;
                var v = ReadString(f, "vcodec");
                var a = ReadString(f, "acodec");
                var ext = ReadString(f, "ext") ?? "";
                var protocol = ReadString(f, "protocol") ?? "";
                var note = ReadString(f, "format_note") ?? "";
                var height = ReadInt(f, "height") ?? 0;
                var h264 = (v ?? "").Contains("h264", StringComparison.OrdinalIgnoreCase) ||
                           (v ?? "").Contains("avc", StringComparison.OrdinalIgnoreCase);
                var hasVideo = !string.IsNullOrWhiteSpace(v) && !v.Equals("none", StringComparison.OrdinalIgnoreCase) || height > 0;
                var hasAudio = !string.Equals(a, "none", StringComparison.OrdinalIgnoreCase);
                var manifest = protocol.Contains("m3u8", StringComparison.OrdinalIgnoreCase) ||
                               protocol.Contains("dash", StringComparison.OrdinalIgnoreCase);
                var watermarked = note.Contains("watermark", StringComparison.OrdinalIgnoreCase);
                if (hasVideo) formats.Add(new Format(id, ext, hasAudio, h264, height, manifest, watermarked));
            }
        }

        var progressive = formats.Where(x => x.HasAudio)
            .OrderBy(x => x.Watermarked).ThenByDescending(x => x.Ext == "mp4")
            .ThenByDescending(x => x.H264).ThenByDescending(x => x.Height).FirstOrDefault();
        string selector;
        bool needsFfmpeg;
        if (progressive is not null)
        {
            selector = progressive.Id;
            needsFfmpeg = progressive.Manifest || progressive.Ext != "mp4";
        }
        else
        {
            var video = formats.OrderBy(x => x.Watermarked).ThenByDescending(x => x.H264)
                .ThenByDescending(x => x.Height).FirstOrDefault();
            if (video is null)
                throw new InvalidOperationException("yt-dlp chỉ tìm thấy audio hoặc không tìm thấy luồng hình ảnh công khai. Không thể tạo MP4 từ audio-only.");
            var audio = ReadAudioFormats(root).OrderByDescending(x => x.Bitrate).FirstOrDefault();
            selector = audio is null ? video.Id : video.Id + "+" + audio.Id;
            needsFfmpeg = true;
        }

        var ffmpeg = FindTool(config["VideoRendering:FfmpegPath"], "ffmpeg.exe", "ffmpeg");
        if (needsFfmpeg && ffmpeg is null)
            throw new InvalidOperationException("Định dạng TikTok cần FFmpeg để ghép/remux. Cấu hình VideoRendering:FfmpegPath.");
        var outputTemplate = Path.Combine(workDirectory, "source.%(ext)s");
        var downloadArgs = new List<string>();
        AddLauncher(downloadArgs, yt);
        downloadArgs.AddRange(["--no-warnings", "--no-progress", "--no-playlist", "--no-cookies",
            "--no-cache-dir", "--socket-timeout", "20", "--retries", "1", "--extractor-retries", "1",
            "--max-filesize", "500M", "--format", selector, "--merge-output-format", "mp4",
            "--output", outputTemplate]);
        if (ffmpeg is not null) { downloadArgs.Add("--ffmpeg-location"); downloadArgs.Add(Path.GetDirectoryName(ffmpeg)!); }
        downloadArgs.Add(pageUrl);

        var downloaded = await RunAsync(yt, downloadArgs, TimeSpan.FromMinutes(5), ct);
        if (downloaded.ExitCode != 0)
            throw new InvalidOperationException("Không tải được luồng video công khai: " + Clip(downloaded.StdErr, 700));

        var file = Directory.GetFiles(workDirectory, "*.mp4", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x => new FileInfo(x).Length > 0);
        if (file is null)
            throw new InvalidOperationException("yt-dlp không tạo được MP4. Có thể nguồn không cung cấp container MP4 hoặc cần FFmpeg.");
        var info = new FileInfo(file);
        if (info.Length > MaxBytes) throw new InvalidOperationException("Video vượt giới hạn 500 MB.");
        var header = new byte[12];
        await using (var stream = File.OpenRead(file))
            if (await stream.ReadAsync(header, ct) < 12 || System.Text.Encoding.ASCII.GetString(header, 4, 4) != "ftyp")
                throw new InvalidOperationException("Tệp tải về không có chữ ký MP4 hợp lệ.");

        var ffprobe = FindTool(config["VideoRendering:FfprobePath"], "ffprobe.exe", "ffprobe");
        if (ffprobe is null && ffmpeg is not null)
        {
            var sibling = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(sibling)) ffprobe = sibling;
        }
        if (ffprobe is null)
            throw new InvalidOperationException("Không tìm thấy ffprobe. Cài bộ FFmpeg đầy đủ hoặc cấu hình VideoRendering:FfprobePath.");
        var probe = await RunAsync(ffprobe,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_type", "-of", "csv=p=0", file],
            TimeSpan.FromSeconds(30), ct);
        if (probe.ExitCode != 0 || !probe.StdOut.Contains("video", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tệp MP4 không có video stream hợp lệ; từ chối lưu asset.");
        return new TikTokDownloadedMedia(title, file);
    }

    private static IEnumerable<AudioFormat> ReadAudioFormats(JsonElement root)
    {
        if (!root.TryGetProperty("formats", out var array) || array.ValueKind != JsonValueKind.Array) yield break;
        foreach (var f in array.EnumerateArray())
        {
            var id = ReadString(f, "format_id");
            var audio = ReadString(f, "acodec");
            var video = ReadString(f, "vcodec");
            if (string.IsNullOrWhiteSpace(id) || string.Equals(audio, "none", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(video) && !video.Equals("none", StringComparison.OrdinalIgnoreCase))) continue;
            yield return new AudioFormat(id, ReadDouble(f, "abr") ?? 0);
        }
    }

    private static async Task<RunResult> RunAsync(string executable, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Không khởi chạy được công cụ media.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException("Công cụ media chạy quá thời gian cho phép.");
        }
        return new RunResult(process.ExitCode, await stdout, await stderr);
    }

    private static void AddLauncher(List<string> args, string executable)
    {
        var name = Path.GetFileName(executable);
        if (name.Equals("py.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("python.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("python", StringComparison.OrdinalIgnoreCase))
        { args.Add("-m"); args.Add("yt_dlp"); }
    }

    private static string? FindTool(string? configured, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var name in names)
            {
                var candidate = Path.Combine(folder.Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    private static string? ReadString(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? ReadInt(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : null;
    private static double? ReadDouble(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : null;
    private static string Clip(string? value, int max) => string.IsNullOrWhiteSpace(value) ? "Không có thông tin lỗi." : value.Length <= max ? value : value[..max];
    private sealed record Format(string Id, string Ext, bool HasAudio, bool H264, int Height, bool Manifest, bool Watermarked);
    private sealed record AudioFormat(string Id, double Bitrate);
    private sealed record RunResult(int ExitCode, string StdOut, string StdErr);
}
