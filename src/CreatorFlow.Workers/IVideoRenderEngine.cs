namespace CreatorFlow.Workers;

internal interface IVideoRenderEngine
{
    string Name { get; }
    Task<VideoRenderResult> RenderAsync(VideoRenderRequest request, CancellationToken cancellationToken);
}

internal sealed record VideoRenderRequest(
    string SourcePath,
    string OutputPath,
    string WorkDirectory,
    string Title,
    string Hook,
    string? CallToAction,
    string StyleKey);

internal sealed record VideoRenderResult(
    string Sha256,
    double DurationSeconds,
    int Width,
    int Height);
