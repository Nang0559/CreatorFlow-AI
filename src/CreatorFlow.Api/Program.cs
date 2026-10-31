using CreatorFlow.Application.Content;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var connectionString = builder.Configuration.GetConnectionString("CreatorFlow");
if (!string.IsNullOrWhiteSpace(connectionString))
    builder.Services.AddDbContext<CreatorFlowDbContext>(options => options.UseSqlServer(connectionString));

var app = builder.Build();
app.UseExceptionHandler();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "CreatorFlow.Api", status = "running", version = "0.1.0" }));
app.MapGet("/api/v1/content-projects", () => Results.Ok(Array.Empty<ContentProjectDto>()));
app.Run();

public partial class Program;
