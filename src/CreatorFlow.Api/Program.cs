using CreatorFlow.Api.Security;
using CreatorFlow.Application.Content;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = ApiKeyAuthenticationHandler.SchemeName;
    options.DefaultChallengeScheme = ApiKeyAuthenticationHandler.SchemeName;
}).AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("CreatorFlow");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<CreatorFlowDbContext>(options => options.UseSqlServer(connectionString));
    builder.Services.AddScoped<IContentProjectRepository, EfContentProjectRepository>();
    builder.Services.AddScoped<CreateContentProjectHandler>();
    builder.Services.AddScoped<ListContentProjectsHandler>();
}

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "CreatorFlow.Api", status = "running", version = "0.3.0" }));

app.MapGet("/api/v1/content-projects", async (int? skip, int? take, HttpContext context, CancellationToken cancellationToken) =>
{
    var handler = context.RequestServices.GetService<ListContentProjectsHandler>();
    if (handler is null)
        return Results.Problem("SQL Server is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 20;
    if (size is < 1 or > 100)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["The page size must be between 1 and 100."] });
    var items = await handler.HandleAsync(page, size, cancellationToken);
    return Results.Ok(items);
}).RequireAuthorization();

app.MapPost("/api/v1/content-projects", async (CreateContentProjectRequest request, HttpContext context, CancellationToken cancellationToken) =>
{
    var handler = context.RequestServices.GetService<CreateContentProjectHandler>();
    if (handler is null)
        return Results.Problem("SQL Server is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required and must be at most 200 characters."] });
    var created = await handler.HandleAsync(new CreateContentProjectCommand(request.Title), cancellationToken);
    return Results.Created($"/api/v1/content-projects/{created.Id}", created);
}).RequireAuthorization();

app.Run();

public sealed record CreateContentProjectRequest(string Title);
public partial class Program;
