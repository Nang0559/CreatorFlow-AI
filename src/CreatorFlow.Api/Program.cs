using CreatorFlow.Api.Security;
using CreatorFlow.Application.Content;
using CreatorFlow.Domain.Discovery;
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
app.MapGet("/", () => Results.Ok(new { service = "CreatorFlow.Api", status = "running", version = "0.4.0" }));

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

// Topics represent the creator's discovery interests and search vocabulary.
var topics = app.MapGroup("/api/v1/topics").RequireAuthorization();
topics.MapGet("/", async (int? skip, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var items = await db.ContentTopics.AsNoTracking().OrderBy(x => x.Name).Skip(page).Take(size)
        .Select(x => new TopicResponse(x.Id, x.Name, x.Keywords, x.IsActive, x.CreatedAt, x.UpdatedAt)).ToArrayAsync(ct);
    return Results.Ok(items);
});
topics.MapPost("/", async (CreateTopicRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    try
    {
        var topic = ContentTopic.Create(request.Name, request.Keywords, DateTimeOffset.UtcNow);
        db.ContentTopics.Add(topic);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/topics/{topic.Id}", new TopicResponse(topic.Id, topic.Name, topic.Keywords, topic.IsActive, topic.CreatedAt, topic.UpdatedAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["topic"] = [ex.Message] }); }
});
topics.MapPut("/{id:guid}", async (Guid id, UpdateTopicRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var topic = await db.ContentTopics.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (topic is null) return Results.NotFound();
    try
    {
        topic.Update(request.Name, request.Keywords, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new TopicResponse(topic.Id, topic.Name, topic.Keywords, topic.IsActive, topic.CreatedAt, topic.UpdatedAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["topic"] = [ex.Message] }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
topics.MapPost("/{id:guid}/deactivate", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var topic = await db.ContentTopics.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (topic is null) return Results.NotFound();
    topic.Deactivate(DateTimeOffset.UtcNow);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

// Channel records are curated by the user. Actual Douyin follow/feed synchronization is a separate connector.
var channels = app.MapGroup("/api/v1/douyin/channels").RequireAuthorization();
channels.MapGet("/", async (bool? followed, int? skip, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var query = db.DouyinSourceChannels.AsNoTracking().AsQueryable();
    if (followed.HasValue) query = query.Where(x => x.IsFollowed == followed.Value);
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var items = await query.OrderBy(x => x.DisplayName).Skip(page).Take(size)
        .Select(x => new ChannelResponse(x.Id, x.DouyinSecUserId, x.DisplayName, x.ProfileUrl, x.IsFollowed, x.FollowedAt, x.LastCheckedAt)).ToArrayAsync(ct);
    return Results.Ok(items);
});
channels.MapPost("/", async (CreateChannelRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (await db.DouyinSourceChannels.AnyAsync(x => x.DouyinSecUserId == request.DouyinSecUserId, ct))
        return Results.Conflict(new { error = "This Douyin channel is already registered." });
    try
    {
        var channel = DouyinSourceChannel.Create(request.DouyinSecUserId, request.DisplayName, request.ProfileUrl, DateTimeOffset.UtcNow);
        db.DouyinSourceChannels.Add(channel);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/douyin/channels/{channel.Id}", new ChannelResponse(channel.Id, channel.DouyinSecUserId, channel.DisplayName, channel.ProfileUrl, channel.IsFollowed, channel.FollowedAt, channel.LastCheckedAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = [ex.Message] }); }
});
channels.MapPost("/{id:guid}/follow", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var channel = await db.DouyinSourceChannels.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (channel is null) return Results.NotFound();
    channel.Follow(DateTimeOffset.UtcNow);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});
channels.MapPost("/{id:guid}/unfollow", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var channel = await db.DouyinSourceChannels.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (channel is null) return Results.NotFound();
    channel.Unfollow(DateTimeOffset.UtcNow);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

// Video intake accepts metadata/links. It deliberately does not claim to download media.
var videos = app.MapGroup("/api/v1/douyin/videos").RequireAuthorization();
videos.MapGet("/", async (string? status, string? discoveryMethod, int? skip, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var query = db.DouyinVideoCandidates.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<VideoCandidateStatus>(status, true, out var parsed)) return Results.BadRequest(new { error = "Unknown video status." });
        query = query.Where(x => x.Status == parsed);
    }
    if (!string.IsNullOrWhiteSpace(discoveryMethod))
    {
        if (!Enum.TryParse<VideoDiscoveryMethod>(discoveryMethod, true, out var parsed)) return Results.BadRequest(new { error = "Unknown discovery method." });
        query = query.Where(x => x.DiscoveryMethod == parsed);
    }
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var items = await query.OrderByDescending(x => x.DiscoveredAt).Skip(page).Take(size)
        .Select(x => new VideoCandidateResponse(x.Id, x.DouyinVideoId, x.SourceChannelId, x.ContentTopicId, x.SourceUrl, x.Title, x.DiscoveryMethod.ToString(), x.Status.ToString(), x.PublishedAt, x.DiscoveredAt)).ToArrayAsync(ct);
    return Results.Ok(items);
});
videos.MapPost("/", async (CreateVideoCandidateRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (await db.DouyinVideoCandidates.AnyAsync(x => x.DouyinVideoId == request.DouyinVideoId, ct))
        return Results.Conflict(new { error = "This Douyin video has already been collected." });
    if (!Enum.TryParse<VideoDiscoveryMethod>(request.DiscoveryMethod, true, out var method))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["discoveryMethod"] = ["Use FollowedChannel, TopicExplore or Manual."] });
    if (request.SourceChannelId.HasValue && !await db.DouyinSourceChannels.AnyAsync(x => x.Id == request.SourceChannelId.Value, ct))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["sourceChannelId"] = ["Source channel does not exist."] });
    if (request.ContentTopicId.HasValue && !await db.ContentTopics.AnyAsync(x => x.Id == request.ContentTopicId.Value, ct))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["contentTopicId"] = ["Content topic does not exist."] });
    try
    {
        var now = DateTimeOffset.UtcNow;
        var video = DouyinVideoCandidate.Create(request.DouyinVideoId, request.SourceChannelId, request.SourceUrl, request.Title, method, request.PublishedAt, now, request.ContentTopicId);
        db.DouyinVideoCandidates.Add(video);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/douyin/videos/{video.Id}", new VideoCandidateResponse(video.Id, video.DouyinVideoId, video.SourceChannelId, video.ContentTopicId, video.SourceUrl, video.Title, video.DiscoveryMethod.ToString(), video.Status.ToString(), video.PublishedAt, video.DiscoveredAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["video"] = [ex.Message] }); }
});
videos.MapPost("/{id:guid}/select", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeVideoStatus(id, db, ct, (video, now) => video.Select(now)));
videos.MapPost("/{id:guid}/reject", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeVideoStatus(id, db, ct, (video, now) => video.Reject(now)));
videos.MapPost("/{id:guid}/queue", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeVideoStatus(id, db, ct, (video, now) => video.QueueForAdaptation(now)));

app.Run();

static async Task<IResult> ChangeVideoStatus(Guid id, CreatorFlowDbContext? db, CancellationToken ct, Action<DouyinVideoCandidate, DateTimeOffset> transition)
{
    if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
    var video = await db.DouyinVideoCandidates.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (video is null) return Results.NotFound();
    try { transition(video, DateTimeOffset.UtcNow); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
}

public sealed record CreateContentProjectRequest(string Title);
public sealed record CreateTopicRequest(string Name, string? Keywords);
public sealed record UpdateTopicRequest(string Name, string? Keywords);
public sealed record TopicResponse(Guid Id, string Name, string Keywords, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreateChannelRequest(string DouyinSecUserId, string DisplayName, string ProfileUrl);
public sealed record ChannelResponse(Guid Id, string DouyinSecUserId, string DisplayName, string ProfileUrl, bool IsFollowed, DateTimeOffset? FollowedAt, DateTimeOffset? LastCheckedAt);
public sealed record CreateVideoCandidateRequest(string DouyinVideoId, Guid? SourceChannelId, Guid? ContentTopicId, string SourceUrl, string? Title, string DiscoveryMethod, DateTimeOffset? PublishedAt);
public sealed record VideoCandidateResponse(Guid Id, string DouyinVideoId, Guid? SourceChannelId, Guid? ContentTopicId, string SourceUrl, string Title, string DiscoveryMethod, string Status, DateTimeOffset? PublishedAt, DateTimeOffset DiscoveredAt);
public partial class Program;
