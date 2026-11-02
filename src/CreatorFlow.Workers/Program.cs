using CreatorFlow.Workers;
using Microsoft.Extensions.Hosting;
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<VideoRenderWorker>();
await builder.Build().RunAsync();
