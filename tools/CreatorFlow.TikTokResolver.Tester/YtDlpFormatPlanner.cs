using System.Globalization;
using System.Text.Json;

/// <summary>
/// Describes a format reported by yt-dlp. A format is considered video only when
/// yt-dlp explicitly reports a video codec or usable video dimensions; the file
/// extension alone is not proof that the payload contains pictures.
/// </summary>
internal sealed record YtDlpFormat(
    string Id, string? Ext, string? VCodec, string? ACodec, string? Protocol,
    int? Width, int? Height, double? Bitrate, string? Url, string? Note)
{
    public bool HasVideo =>
        (!string.IsNullOrWhiteSpace(VCodec) && !VCodec.Equals("none", StringComparison.OrdinalIgnoreCase)) ||
        Height is > 0 || Width is > 0;

    public bool HasAudio => !string.Equals(ACodec, "none", StringComparison.OrdinalIgnoreCase);
    public bool IsVideoOnly => HasVideo && string.Equals(ACodec, "none", StringComparison.OrdinalIgnoreCase);
    public bool IsAudioOnly => !HasVideo && HasAudio;
    public bool IsManifest => Protocol is not null &&
        (Protocol.StartsWith("m3u8", StringComparison.OrdinalIgnoreCase) ||
         Protocol.StartsWith("http_dash", StringComparison.OrdinalIgnoreCase) ||
         Protocol.Contains("dash", StringComparison.OrdinalIgnoreCase));
    public bool IsMp4Family => (Ext ?? "").ToLowerInvariant() is "mp4" or "m4v" or "mov";
    public bool IsWatermarked => Id.Equals("download", StringComparison.OrdinalIgnoreCase) ||
        (Note?.Contains("watermark", StringComparison.OrdinalIgnoreCase) ?? false);
    public bool IsH264 => (VCodec ?? "").StartsWith("avc", StringComparison.OrdinalIgnoreCase) ||
        (VCodec ?? "").StartsWith("h264", StringComparison.OrdinalIgnoreCase);
    public string Summary => string.Create(CultureInfo.InvariantCulture,
        $"{Id}: ext={Ext ?? "?"} v={VCodec ?? "?"} a={ACodec ?? "?"} {Width?.ToString() ?? "?"}x{Height?.ToString() ?? "?"} proto={Protocol ?? "?"}");
}

internal sealed record YtDlpDownloadPlan(
    bool Success, string? FormatSelector, string ErrorCode, string? Error,
    bool NeedsFfmpeg, string? Warning, string? ChosenFormatId, string? PreviewUrl,
    IReadOnlyList<string> Formats);

internal static class YtDlpFormatPlanner
{
    public const string NoVideoStream = "no-video-stream";
    public const string FfmpegRequired = "ffmpeg-required";

    public static IReadOnlyList<YtDlpFormat> ParseFormats(JsonElement root)
    {
        var list = new List<YtDlpFormat>();
        if (root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in formats.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && ReadFormat(item) is { } format) list.Add(format);
        }
        else if (ReadFormat(root) is { } single) list.Add(single);
        return list;
    }

    public static bool IsImagePlaylist(JsonElement root) =>
        root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array &&
        !(root.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array && formats.GetArrayLength() > 0);

    public static YtDlpDownloadPlan Plan(IReadOnlyList<YtDlpFormat> formats, bool ffmpegAvailable)
    {
        var summaries = formats.Select(f => f.Summary).ToArray();
        var video = formats.Where(f => f.HasVideo && (ffmpegAvailable || !f.IsManifest)).ToList();
        if (!formats.Any(f => f.HasVideo))
        {
            var kinds = formats.Count == 0 ? "không có luồng media nào" :
                "chỉ có luồng âm thanh (" + string.Join(", ", formats.Select(f => f.Ext ?? "?").Distinct()) + ")";
            return Fail(NoVideoStream,
                "yt-dlp báo " + kinds + " cho link này. Có thể đây là bài đăng ảnh/slideshow hoặc TikTok không cung cấp luồng hình ảnh công khai. FFmpeg không thể tạo hình ảnh từ âm thanh.", summaries);
        }

        var progressive = video.Where(f => f.HasAudio)
            .OrderBy(f => f.IsWatermarked)
            .ThenByDescending(f => f.IsMp4Family)
            .ThenByDescending(f => f.IsH264)
            .ThenByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => f.Bitrate ?? 0)
            .FirstOrDefault();
        if (progressive is not null)
        {
            var needs = progressive.IsManifest || !progressive.IsMp4Family;
            if (!needs || ffmpegAvailable)
                return Ok(progressive.Id, needs, null, progressive, summaries);
        }

        var videoOnly = video.Where(f => f.IsVideoOnly)
            .OrderByDescending(f => f.IsH264).ThenByDescending(f => f.Height ?? 0)
            .ThenByDescending(f => f.Bitrate ?? 0).FirstOrDefault();
        var audioOnly = formats.Where(f => f.IsAudioOnly && (ffmpegAvailable || !f.IsManifest))
            .OrderByDescending(f => f.Bitrate ?? 0).FirstOrDefault();

        if (videoOnly is not null && audioOnly is not null)
        {
            if (!ffmpegAvailable)
                return Fail(FfmpegRequired, "Video và âm thanh nằm ở hai luồng riêng; cần FFmpeg để ghép thành MP4. Cài FFmpeg rồi khởi động lại tester.", summaries);
            return Ok(videoOnly.Id + "+" + audioOnly.Id, true, null, videoOnly, summaries);
        }

        if (videoOnly is not null)
        {
            var needs = videoOnly.IsManifest || !videoOnly.IsMp4Family;
            if (needs && !ffmpegAvailable)
                return Fail(FfmpegRequired, "Luồng video cần FFmpeg để chuyển container sang MP4.", summaries);
            return Ok(videoOnly.Id, needs, "Nguồn chỉ có luồng hình ảnh; MP4 tải về sẽ không có tiếng.", videoOnly, summaries);
        }

        return Fail(NoVideoStream, "Không chọn được định dạng video có thể tải.", summaries);
    }

    private static YtDlpDownloadPlan Ok(string selector, bool needsFfmpeg, string? warning,
        YtDlpFormat primary, string[] summaries) =>
        new(true, selector, "", null, needsFfmpeg, warning, primary.Id,
            !primary.IsManifest && primary.HasAudio ? primary.Url : null, summaries);

    private static YtDlpDownloadPlan Fail(string code, string error, string[] summaries) =>
        new(false, null, code, error, false, null, null, null, summaries);

    private static YtDlpFormat? ReadFormat(JsonElement element)
    {
        var id = ReadString(element, "format_id");
        if (string.IsNullOrWhiteSpace(id)) return null;
        return new YtDlpFormat(id, ReadString(element, "ext"), ReadString(element, "vcodec"),
            ReadString(element, "acodec"), ReadString(element, "protocol"), ReadInt(element, "width"),
            ReadInt(element, "height"), ReadDouble(element, "tbr"), ReadString(element, "url"),
            ReadString(element, "format_note"));
    }

    private static string? ReadString(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? ReadInt(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : null;
    private static double? ReadDouble(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : null;
}
