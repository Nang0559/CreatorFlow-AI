using CreatorFlow.Workers;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var engineName = builder.Configuration["VideoRendering:Engine"] ?? "ffmpeg";
if (!string.Equals(engineName, "ffmpeg", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException($"Video rendering engine '{engineName}' is not installed. Supported engine: ffmpeg.");

builder.Services.AddSingleton<IVideoRenderEngine, FfmpegVideoRenderEngine>();
builder.Services.AddHostedService<VideoRenderWorker>();
builder.Services.AddHostedService<DiscoveryTopicSchedulerWorker>();
await builder.Build().RunAsync();
