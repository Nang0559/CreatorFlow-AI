using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace CreatorFlow.Workers;

/// <summary>
/// Native FFmpeg renderer. It deliberately has no dependency on After Effects.
/// Future render providers implement IVideoRenderEngine without changing queue orchestration.
/// </summary>
internal sealed class FfmpegVideoRenderEngine(IConfiguration config, ILogger<FfmpegVideoRenderEngine> logger) : IVideoRenderEngine
{
    private readonly string ffmpeg = config["VideoRendering:FFmpegExe"] ?? "ffmpeg.exe";
    private readonly string ffprobe = config["VideoRendering:FFprobeExe"] ?? "ffprobe.exe";
    private readonly string fontFile = config["VideoRendering:FontFile"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");

    public string Name => "ffmpeg";

    public async Task<VideoRenderResult> RenderAsync(VideoRenderRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(request.WorkDirectory);
        if (!File.Exists(request.SourcePath)) throw new FileNotFoundException("Không tìm thấy video nguồn.", request.SourcePath);
        Require(ffmpeg);
        Require(ffprobe);
        Require(fontFile);

        var source = await ProbeAsync(request.SourcePath, cancellationToken);
        if (source.Duration < 1 || source.Duration > 600 || source.Width < 16 || source.Height < 16)
            throw new InvalidOperationException("Video nguồn không hợp lệ hoặc dài hơn giới hạn xử lý 10 phút.");

        var titleFile = WriteText(request.WorkDirectory, "title.txt", request.Title);
        var hookFile = WriteText(request.WorkDirectory, "hook.txt", request.Hook);
        var ctaFile = WriteText(request.WorkDirectory, "cta.txt", request.CallToAction ?? string.Empty);
        var style = ResolveStyle(request.StyleKey);
        var filters = new List<string>
        {
            "scale=1080:1920:force_original_aspect_ratio=increase",
            "crop=1080:1920",
            "setsar=1"
        };
        AddTextFilter(filters, titleFile, style.TitleY, style.TitleSize, style.TitleColor, 0, Math.Min(5, source.Duration));
        AddTextFilter(filters, hookFile, style.HookY, style.HookSize, style.HookColor, 0.35, Math.Min(7, source.Duration));
        if (!string.IsNullOrWhiteSpace(request.CallToAction))
            AddTextFilter(filters, ctaFile, 1660, 46, style.CtaColor, Math.Max(0, source.Duration - 4), source.Duration);

        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", request.SourcePath,
            "-vf", string.Join(",", filters),
            "-map", "0:v:0", "-map", "0:a?",
            "-c:v", "libx264", "-preset", "medium", "-crf", "21",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
            "-t", source.Duration.ToString("0.###", CultureInfo.InvariantCulture),
            "-movflags", "+faststart", request.OutputPath
        };
        await RunAsync(ffmpeg, args, request.WorkDirectory, TimeSpan.FromMinutes(60), cancellationToken);
        if (!File.Exists(request.OutputPath) || new FileInfo(request.OutputPath).Length < 50_000)
            throw new InvalidOperationException("MP4 đầu ra không hợp lệ.");

        var output = await ProbeAsync(request.OutputPath, cancellationToken);
        if (output.Width != 1080 || output.Height != 1920)
            throw new InvalidOperationException("Kích thước MP4 không đúng chuẩn 1080×1920.");
        await using var stream = File.OpenRead(request.OutputPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        logger.LogInformation("Đã render MP4 bằng {Engine}: {OutputPath}", Name, request.OutputPath);
        return new VideoRenderResult(hash, output.Duration, output.Width, output.Height);
    }

    private void AddTextFilter(List<string> filters, string textFile, int y, int size, string color, double start, double end)
    {
        if (end <= start) return;
        filters.Add($"drawtext=fontfile='{EscapeFilterPath(fontFile)}':textfile='{EscapeFilterPath(textFile)}':reload=0:expansion=none:fontcolor={color}:fontsize={size}:x=(w-text_w)/2:y={y}:borderw=3:bordercolor=black@0.82:shadowcolor=black@0.45:shadowx=2:shadowy=2:alpha='if(lt(t\\,{start.ToString("0.###",CultureInfo.InvariantCulture)})\\,0\\,if(lt(t\\,{(start+0.35).ToString("0.###",CultureInfo.InvariantCulture)})\\,(t-{start.ToString("0.###",CultureInfo.InvariantCulture)})/0.35\\,1))':enable='between(t\\,{start.ToString("0.###",CultureInfo.InvariantCulture)}\\,{end.ToString("0.###",CultureInfo.InvariantCulture)})'");
    }

    private static string EscapeFilterPath(string path) => Path.GetFullPath(path).Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");

    private static string WriteText(string directory, string name, string value)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, value.Replace("\r", " ").Replace("\n", " "), new System.Text.UTF8Encoding(false));
        return path;
    }

    private static (int TitleY, int TitleSize, string TitleColor, int HookY, int HookSize, string HookColor, string CtaColor) ResolveStyle(string key) =>
        key switch
        {
            "Đánh giá sản phẩm" => (250, 64, "0xFFD166", 1390, 58, "white", "0xFFD166"),
            "Kể chuyện" => (300, 70, "0xD1B3FF", 680, 58, "white", "0xD1B3FF"),
            _ => (1320, 64, "white", 1510, 52, "white", "0xD1B3FF")
        };

    private async Task<(double Duration, int Width, int Height)> ProbeAsync(string path, CancellationToken ct)
    {
        var json = await RunCaptureAsync(ffprobe,
            ["-v","error","-select_streams","v:0","-show_entries","stream=width,height:format=duration","-of","json",path],
            Path.GetDirectoryName(path)!, TimeSpan.FromSeconds(30), ct);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new InvalidOperationException("Video không có luồng hình ảnh.");
        var format = doc.RootElement.GetProperty("format");
        return (double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture),
            streams[0].GetProperty("width").GetInt32(), streams[0].GetProperty("height").GetInt32());
    }

    private static async Task RunAsync(string exe, IEnumerable<string> args, string cwd, TimeSpan timeout, CancellationToken ct)
    {
        var error = await RunCaptureAsync(exe, args, cwd, timeout, ct, captureOutput: false);
        _ = error;
    }

    private static async Task<string> RunCaptureAsync(string exe, IEnumerable<string> args, string cwd, TimeSpan timeout, CancellationToken ct, bool captureOutput = true)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe)
            {
                WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);
        try { await process.WaitForExitAsync(linked.Token); }
        catch { try { process.Kill(true); } catch { } throw; }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} mã {process.ExitCode}: {stderr[..Math.Min(stderr.Length, 1600)]}");
        return captureOutput ? stdout : string.Empty;
    }

    private static void Require(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Chưa cấu hình hoặc không tìm thấy công cụ dựng video.", path);
    }
}
