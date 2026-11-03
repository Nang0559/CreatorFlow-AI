using CreatorFlow.Api.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Text;
using System.IO.Compression;
using CreatorFlow.Api.Security;
using CreatorFlow.Api.Video;
using CreatorFlow.Application.Content;
using CreatorFlow.Domain.Discovery;
using CreatorFlow.Domain.Content;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = 524_288_000);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 524_288_000);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient<CreatorFlowAiClient>();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
builder.Services.AddTransient<TenantContextMiddleware>();
builder.Services.AddScoped<IAuthorizationHandler, TenantMembershipAuthorizationHandler>();

var oidcAuthority = builder.Configuration["Authentication:Oidc:Authority"];
var oidcAudience = builder.Configuration["Authentication:Oidc:Audience"];
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "CreatorFlow";
    options.DefaultAuthenticateScheme = "CreatorFlow";
    options.DefaultChallengeScheme = "CreatorFlow";
})
.AddPolicyScheme("CreatorFlow", "OIDC bearer or local API key", options =>
{
    options.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? JwtBearerDefaults.AuthenticationScheme
            : ApiKeyAuthenticationHandler.SchemeName;
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationHandler.SchemeName, _ => { })
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Authority = oidcAuthority;
    options.Audience = oidcAudience;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization(options =>
{
    var tenantPolicy = new AuthorizationPolicyBuilder("CreatorFlow")
        .RequireAuthenticatedUser()
        .AddRequirements(new TenantMembershipRequirement())
        .Build();
    options.DefaultPolicy = tenantPolicy;
    options.FallbackPolicy = tenantPolicy;
});

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
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<TenantRoleGuardMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Ok(new { service = "CreatorFlow.Api", status = "running", version = "0.4.0" })).AllowAnonymous();
app.MapVideoRenderEndpoints();

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
topics.MapPost("/{id:guid}/discovery-plan", async (Guid id, CreateTopicDiscoveryPlanRequest request, CreatorFlowDbContext? db, CreatorFlowAiClient aiClient, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var topic = await db.ContentTopics.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
    if (topic is null) return Results.NotFound();
    var keywords = topic.Keywords.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToArray();
    var payload = new AiDiscoveryQueryPlanRequest(topic.Name, keywords, "Vietnam",
        string.IsNullOrWhiteSpace(request.ProductContext) ? null : request.ProductContext.Trim(),
        string.IsNullOrWhiteSpace(request.AudienceContext) ? null : request.AudienceContext.Trim(),
        Math.Clamp(request.MaxQueries ?? 8, 2, 12));
    return await aiClient.ForwardAsync("/v1/discovery/query-plan", payload, ct);
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

    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    var now = DateTimeOffset.UtcNow;
    channel.Follow(now);
    await db.SaveChangesAsync(ct);

    // Create or re-enable the durable scan schedule. No scan is executed until an
    // approved provider connector and the discovery worker are installed.
    await db.Database.ExecuteSqlInterpolatedAsync($@"
        IF EXISTS (SELECT 1 FROM dbo.DiscoveryChannelSyncStates WHERE TenantId =
            (SELECT TenantId FROM dbo.DouyinSourceChannels WHERE Id = {id}) AND ChannelId = {id})
            UPDATE dbo.DiscoveryChannelSyncStates
               SET IsEnabled = 1, NextScanAt = DATEADD(minute, PollIntervalMinutes, SYSUTCDATETIME()),
                   UpdatedAt = SYSUTCDATETIME()
             WHERE TenantId = (SELECT TenantId FROM dbo.DouyinSourceChannels WHERE Id = {id})
               AND ChannelId = {id};
        ELSE
            INSERT dbo.DiscoveryChannelSyncStates
                (Id, TenantId, ChannelId, IsEnabled, PollIntervalMinutes, NextScanAt, UpdatedAt)
            SELECT NEWID(), TenantId, Id, 1, 30, DATEADD(minute, 30, SYSUTCDATETIME()), SYSUTCDATETIME()
              FROM dbo.DouyinSourceChannels WHERE Id = {id};", ct);
    await transaction.CommitAsync(ct);
    return Results.Ok(new { followed = true, scanSchedule = "Enabled", scanStatus = "WaitingForApprovedConnector" });
});
channels.MapPost("/{id:guid}/unfollow", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var channel = await db.DouyinSourceChannels.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (channel is null) return Results.NotFound();

    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    channel.Unfollow(DateTimeOffset.UtcNow);
    await db.SaveChangesAsync(ct);
    await db.Database.ExecuteSqlInterpolatedAsync($@"
        UPDATE dbo.DiscoveryChannelSyncStates
           SET IsEnabled = 0, UpdatedAt = SYSUTCDATETIME()
         WHERE TenantId = (SELECT TenantId FROM dbo.DouyinSourceChannels WHERE Id = {id})
           AND ChannelId = {id};", ct);
    await transaction.CommitAsync(ct);
    return Results.Ok(new { followed = false, scanSchedule = "Disabled" });
});



// Share inbox: accept only creator-submitted HTTPS links; never fetch the supplied URL server-side.
var shareInbox = app.MapGroup("/api/v1/share-inbox").RequireAuthorization();
shareInbox.MapGet("/", async (string? status, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var query = db.SharedLinkInboxItems.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<SharedLinkStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            return Results.BadRequest(new { error = "Unknown share inbox status." });
        query = query.Where(x => x.Status == parsed);
    }
    var items = await query.OrderByDescending(x => x.SharedAt).Take(size)
        .Select(x => new SharedLinkInboxResponse(x.Id, x.Platform, x.SharedUrl, x.SharedAt,
            x.Status.ToString(), x.ReviewNote, x.LinkedChannelId)).ToArrayAsync(ct);
    return Results.Ok(items);
});
shareInbox.MapPost("/", async (CreateSharedLinkRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    try
    {
        var item = SharedLinkInboxItem.Create(request.SharedUrl, DateTimeOffset.UtcNow);
        var existing = await db.SharedLinkInboxItems.AsNoTracking().SingleOrDefaultAsync(x => x.UrlHash == item.UrlHash, ct);
        if (existing is not null)
            return Results.Ok(new SharedLinkInboxResponse(existing.Id, existing.Platform, existing.SharedUrl,
                existing.SharedAt, existing.Status.ToString(), existing.ReviewNote, existing.LinkedChannelId));
        db.SharedLinkInboxItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/share-inbox/{item.Id}",
            new SharedLinkInboxResponse(item.Id, item.Platform, item.SharedUrl, item.SharedAt,
                item.Status.ToString(), item.ReviewNote, item.LinkedChannelId));
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["sharedUrl"] = [ex.Message] });
    }
    catch (DbUpdateException)
    {
        db.ChangeTracker.Clear();
        var existing = await db.SharedLinkInboxItems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UrlHash == SharedLinkInboxItem.Create(request.SharedUrl, DateTimeOffset.UtcNow).UrlHash, ct);
        if (existing is not null)
            return Results.Ok(new SharedLinkInboxResponse(existing.Id, existing.Platform, existing.SharedUrl,
                existing.SharedAt, existing.Status.ToString(), existing.ReviewNote, existing.LinkedChannelId));
        throw;
    }
});
shareInbox.MapPost("/{id:guid}/review", async (Guid id, ReviewSharedLinkRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var item = await db.SharedLinkInboxItems.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (item is null) return Results.NotFound();
    try { item.MarkReviewed(request.Note); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = [ex.Message] }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
shareInbox.MapPost("/{id:guid}/ignore", async (Guid id, ReviewSharedLinkRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var item = await db.SharedLinkInboxItems.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (item is null) return Results.NotFound();
    try { item.Ignore(request.Note); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = [ex.Message] }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
shareInbox.MapPost("/{id:guid}/link-channel/{channelId:guid}", async (Guid id, Guid channelId, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var item = await db.SharedLinkInboxItems.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (item is null) return Results.NotFound();
    if (!await db.DouyinSourceChannels.AnyAsync(x => x.Id == channelId, ct)) return Results.NotFound(new { error = "Channel not found." });
    try { item.LinkChannel(channelId); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["channelId"] = [ex.Message] }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});


var trendFeedback = app.MapGroup("/api/v1/douyin/videos/{id:guid}/feedback").RequireAuthorization();
trendFeedback.MapGet("/", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!await db.DouyinVideoCandidates.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
    var items = await db.TrendSelectionFeedback.AsNoTracking().Where(x => x.DouyinVideoCandidateId == id)
        .OrderByDescending(x => x.ReviewedAt)
        .Select(x => new TrendSelectionFeedbackResponse(x.Id, x.DouyinVideoCandidateId, x.Decision.ToString(),
            x.ReasonCode, x.ReasonNote, x.CandidateSnapshotJson, x.FeatureSnapshotJson, x.ReviewedAt)).ToArrayAsync(ct);
    return Results.Ok(items);
});
trendFeedback.MapPost("/", async (Guid id, CreateTrendSelectionFeedbackRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var candidate = await db.DouyinVideoCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
    if (candidate is null) return Results.NotFound();
    if (!Enum.TryParse<TrendSelectionDecision>(request.Decision, true, out var decision) ||
        !Enum.IsDefined(decision))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["decision"] = ["Use Select, Reject or Watch."] });
    try
    {
        var snapshot = JsonSerializer.Serialize(new { candidate.DouyinVideoId, candidate.SourceUrl, candidate.Title,
            candidate.DiscoveryMethod, candidate.Status, candidate.PublishedAt, candidate.DiscoveredAt });
        var reviewedAt = DateTimeOffset.UtcNow;
        var featureSnapshot = TrendSelectionFeatureSnapshot.Create(candidate, reviewedAt).ToJson();
        var feedback = TrendSelectionFeedback.Create(id, decision, request.ReasonCode, request.ReasonNote,
            snapshot, featureSnapshot, reviewedAt);
        db.TrendSelectionFeedback.Add(feedback);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/douyin/videos/{id}/feedback",
            new TrendSelectionFeedbackResponse(feedback.Id, id, feedback.Decision.ToString(), feedback.ReasonCode,
                feedback.ReasonNote, feedback.CandidateSnapshotJson, feedback.FeatureSnapshotJson, feedback.ReviewedAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["feedback"] = [ex.Message] }); }
});

var trendOutcomes = app.MapGroup("/api/v1/douyin/videos/{id:guid}/feedback/{feedbackId:guid}/outcomes").RequireAuthorization();
trendOutcomes.MapGet("/", async (Guid id, Guid feedbackId, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var feedbackExists = await db.TrendSelectionFeedback.AnyAsync(
        x => x.Id == feedbackId && x.DouyinVideoCandidateId == id, ct);
    if (!feedbackExists) return Results.NotFound();
    var items = await db.TrendCandidateOutcomeObservations.AsNoTracking()
        .Where(x => x.FeedbackId == feedbackId)
        .OrderByDescending(x => x.ObservedAt)
        .Select(x => new TrendCandidateOutcomeObservationResponse(x.Id, x.FeedbackId,
            x.OutcomeKind.ToString(), x.OutcomeValue, x.MetricsSnapshotJson, x.Source, x.ObservedAt))
        .ToArrayAsync(ct);
    return Results.Ok(items);
});
trendOutcomes.MapPost("/", async (Guid id, Guid feedbackId, CreateTrendCandidateOutcomeObservationRequest request,
    CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var feedbackExists = await db.TrendSelectionFeedback.AnyAsync(
        x => x.Id == feedbackId && x.DouyinVideoCandidateId == id, ct);
    if (!feedbackExists) return Results.NotFound();
    if (!Enum.TryParse<TrendCandidateOutcomeKind>(request.OutcomeKind, true, out var outcomeKind) ||
        !Enum.IsDefined(outcomeKind))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["outcomeKind"] = ["Use Adapted, Published, MarketPerformance or CreatorAssessment."] });
    try
    {
        var outcome = TrendCandidateOutcomeObservation.Create(feedbackId, outcomeKind,
            request.OutcomeValue, request.MetricsSnapshotJson, request.Source, DateTimeOffset.UtcNow);
        db.TrendCandidateOutcomeObservations.Add(outcome);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/douyin/videos/{id}/feedback/{feedbackId}/outcomes",
            new TrendCandidateOutcomeObservationResponse(outcome.Id, feedbackId, outcome.OutcomeKind.ToString(),
                outcome.OutcomeValue, outcome.MetricsSnapshotJson, outcome.Source, outcome.ObservedAt));
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["outcome"] = [ex.Message] });
    }
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


// Vietnam market trend observations: creator channels, videos and sounds are separate signal kinds.
var vietnamTrends = app.MapGroup("/api/v1/vietnam-trends").RequireAuthorization();
vietnamTrends.MapGet("/", async (string? kind, string? platform, int? skip, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var query = db.VietnamTrendSignals.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(kind))
    {
        if (!Enum.TryParse<TrendSignalKind>(kind, true, out var parsed)) return Results.BadRequest(new { error = "Unknown trend kind." });
        query = query.Where(x => x.Kind == parsed);
    }
    if (!string.IsNullOrWhiteSpace(platform))
    {
        if (!Enum.TryParse<TrendPlatform>(platform, true, out var parsed)) return Results.BadRequest(new { error = "Unknown platform." });
        query = query.Where(x => x.Platform == parsed);
    }
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var items = await query.OrderByDescending(x => x.ObservedAt).Skip(page).Take(size)
        .Select(x => new VietnamTrendResponse(x.Id, x.Kind.ToString(), x.Platform.ToString(), x.ExternalId,
            x.Title, x.CreatorName, x.SourceUrl, x.Topic, x.SoundName, x.SoundArtist,
            x.Views, x.Likes, x.Comments, x.Shares, x.Saves, x.PublishedAt, x.ObservedAt)).ToArrayAsync(ct);
    return Results.Ok(items);
});
vietnamTrends.MapPost("/", async (CreateVietnamTrendRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!Enum.TryParse<TrendSignalKind>(request.Kind, true, out var kind))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Use CreatorChannel, Video or Sound."] });
    if (!Enum.TryParse<TrendPlatform>(request.Platform, true, out var platform))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["platform"] = ["Unknown platform."] });
    if (await db.VietnamTrendSignals.AnyAsync(x => x.Platform == platform && x.Kind == kind && x.ExternalId == request.ExternalId, ct))
        return Results.Conflict(new { error = "This trend signal is already registered; record a new observation instead." });
    try
    {
        var now = DateTimeOffset.UtcNow;
        var signal = VietnamTrendSignal.Create(kind, platform, request.ExternalId, request.Title,
            request.CreatorName, request.SourceUrl, request.Topic, request.SoundName, request.SoundArtist,
            request.Views, request.Likes, request.Comments, request.Shares, request.Saves, request.PublishedAt, now);
        db.VietnamTrendSignals.Add(signal);
        db.VietnamTrendMetricSnapshots.Add(VietnamTrendMetricSnapshot.Create(signal.Id, signal.Views, signal.Likes,
            signal.Comments, signal.Shares, signal.Saves, signal.ObservedAt));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/vietnam-trends/{signal.Id}",
            new VietnamTrendResponse(signal.Id, signal.Kind.ToString(), signal.Platform.ToString(), signal.ExternalId,
                signal.Title, signal.CreatorName, signal.SourceUrl, signal.Topic, signal.SoundName, signal.SoundArtist,
                signal.Views, signal.Likes, signal.Comments, signal.Shares, signal.Saves, signal.PublishedAt, signal.ObservedAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["trend"] = [ex.Message] }); }
});
vietnamTrends.MapGet("/{id:guid}/history", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!await db.VietnamTrendSignals.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
    var snapshots = await db.VietnamTrendMetricSnapshots.AsNoTracking().Where(x => x.TrendSignalId == id)
        .OrderBy(x => x.ObservedAt).Select(x => new { x.Views, x.Likes, x.Comments, x.Shares, x.Saves, x.ObservedAt }).ToArrayAsync(ct);
    return Results.Ok(snapshots);
});
vietnamTrends.MapPost("/{id:guid}/observe", async (Guid id, RecordVietnamTrendObservationRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var signal = await db.VietnamTrendSignals.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (signal is null) return Results.NotFound();
    try
    {
        var observedAt = DateTimeOffset.UtcNow;
        signal.RecordObservation(request.Views, request.Likes, request.Comments, request.Shares, request.Saves, observedAt);
        db.VietnamTrendMetricSnapshots.Add(VietnamTrendMetricSnapshot.Create(signal.Id, request.Views,
            request.Likes, request.Comments, request.Shares, request.Saves, observedAt));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
    catch (ArgumentOutOfRangeException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["metrics"] = [ex.Message] }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});

// Style Lab: each experiment compares two or three independently authored variants.
var styleLab = app.MapGroup("/api/v1/style-experiments").RequireAuthorization();
styleLab.MapGet("/", async (string? status, int? skip, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var query = db.StyleExperiments.AsNoTracking().Include(x => x.Variants).AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<StyleExperimentStatus>(status, true, out var parsed)) return Results.BadRequest(new { error = "Unknown experiment status." });
        query = query.Where(x => x.Status == parsed);
    }
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var items = await query.OrderByDescending(x => x.UpdatedAt).Skip(page).Take(size)
        .Select(x => new StyleExperimentResponse(x.Id, x.ContentProjectId, x.Name, x.Hypothesis, x.Topic, x.Status.ToString(),
            x.WinningVariantId, x.Variants.OrderBy(v => v.Name).Select(v => new StyleVariantResponse(v.Id,
                v.Name, v.Hook, v.SpeakingStyle, v.EditPacing, v.AudioApproach, v.Script, v.Status.ToString(),
                v.Views, v.Likes, v.Comments, v.Shares, v.Saves, v.FollowsGained, v.ProductClicks, v.Orders,
                v.AverageWatchSeconds, v.VideoDurationSeconds, v.EngagementRate, v.PublishedAt, v.MeasuredAt)).ToArray()))
        .ToArrayAsync(ct);
    return Results.Ok(items);
});

styleLab.MapGet("/{id:guid}/variants/{variantId:guid}/editing-package", async (Guid id, Guid variantId, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var experiment = await db.StyleExperiments.AsNoTracking().Include(x => x.Variants)
        .SingleOrDefaultAsync(x => x.Id == id, ct);
    if (experiment is null) return Results.NotFound();
    var variant = experiment.Variants.SingleOrDefault(x => x.Id == variantId);
    if (variant is null) return Results.NotFound();

    var plan = new
    {
        schemaVersion = "creatorflow-edit-plan-v1",
        experimentId = experiment.Id,
        experiment = experiment.Name,
        hypothesis = experiment.Hypothesis,
        topic = experiment.Topic,
        variantId = variant.Id,
        variant = variant.Name,
        format = new { width = 1080, height = 1920, fps = 30, aspectRatio = "9:16" },
        hook = variant.Hook,
        speakingStyle = variant.SpeakingStyle,
        editPacing = variant.EditPacing,
        audioApproach = variant.AudioApproach,
        script = variant.Script,
        creativeNotes = variant.CreativeNotes,
        mediaPolicy = "Use original footage or media you are authorized to use. This package contains no copied source video, licensed music or third-party effects."
    };
    var planJson = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
    var safeName = new string(variant.Name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
    var readme = $"""
CREATORFLOW – VIDEO EDIT HANDOFF
Experiment: {experiment.Name}
Variant: {variant.Name}
Topic: {experiment.Topic ?? "Not specified"}

CAPCUT DESKTOP
1. Create a new 9:16 project (1080 x 1920); use 30 fps unless your source footage requires another rate.
2. Import your own/authorized footage and audio.
3. Follow shot-list.csv and script.txt to assemble the timeline.
4. Apply CapCut transitions, effects, typography, color and sound design manually.
5. Review product claims, music rights, captions and platform requirements before export.

AFTER EFFECTS
1. Open After Effects.
2. Choose File > Scripts > Run Script File and select after-effects/CreatorFlow-AE-Starter.jsx.
3. The script creates an editable 9:16 composition with hook, script and shot placeholders, then asks where to save the .aep project.
4. Replace shot placeholders with your own footage and apply your preferred effects/plugins.
5. Render and review in After Effects. The starter does not pretend to recreate CapCut's proprietary effects or third-party plugins.

This is an editable handoff/starter package, not a finished MP4 and not a CapCut project file.
""";
    var shotCsv = new StringBuilder("Shot,Visual direction,Edit note\n");
    for (var i = 1; i <= 5; i++)
        shotCsv.Append(i).Append(",").Append(Csv(i == 1 ? "Opening visual / product context" : i == 5 ? "Result / closing visual" : "Demonstration detail / supporting footage"))
            .Append(",").Append(Csv(variant.EditPacing)).Append('\n');

    var hookJs = JsonSerializer.Serialize(variant.Hook);
    var scriptJs = JsonSerializer.Serialize(variant.Script);
    var nameJs = JsonSerializer.Serialize(experiment.Name + " – " + variant.Name);
    var jsx = string.Join(Environment.NewLine, new[]
    {
        "// CreatorFlow After Effects editable starter. No third-party effects are bundled.",
        "app.beginUndoGroup('CreatorFlow – Build editable starter');",
        "if (!app.project) app.newProject();",
        $"var comp = app.project.items.addComp({nameJs}, 1080, 1920, 1, 45, 30);",
        "function addText(txt, layerName, y, size) {",
        "  var layer = comp.layers.addText(txt); layer.name = layerName;",
        "  var prop = layer.property('Source Text'); var doc = prop.value;",
        "  doc.fontSize = size; doc.fillColor = [0.94, 0.91, 1.0]; prop.setValue(doc);",
        "  layer.property('Transform').property('Position').setValue([540, y]); return layer;",
        "}",
        $"addText({hookJs}, '01 – Hook', 260, 64);",
        $"addText({scriptJs}, '02 – Script – replace with timed captions', 720, 34);",
        "addText('SHOT 01 – Replace with authorized footage', '03 – Shot placeholder 01', 1120, 28);",
        "addText('SHOT 02 – Replace with authorized footage', '04 – Shot placeholder 02', 1280, 28);",
        "addText('SHOT 03 – Replace with authorized footage', '05 – Shot placeholder 03', 1440, 28);",
        "addText('CREATORFLOW – END CARD', '06 – End card', 1760, 42);",
        "comp.openInViewer();",
        "var folder = Folder.selectDialog('Choose where to save your CreatorFlow After Effects starter');",
        "if (folder) app.project.save(File(folder.fsName + '/CreatorFlow_AE_Starter.aep'));",
        "app.endUndoGroup();"
    });

    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
    {
        void AddEntry(string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }
        AddEntry("README-CapCut-AfterEffects.txt", readme);
        AddEntry("edit-plan.json", planJson);
        AddEntry("script.txt", variant.Script);
        AddEntry("shot-list.csv", shotCsv.ToString());
        AddEntry("after-effects/CreatorFlow-AE-Starter.jsx", jsx);
    }
    return Results.File(memory.ToArray(), "application/zip", $"CreatorFlow_{safeName}_EditingPackage.zip");
});

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

styleLab.MapPost("/", async (CreateStyleExperimentRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (request.ContentProjectId.HasValue && !await db.ContentProjects.AnyAsync(x => x.Id == request.ContentProjectId.Value, ct))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["contentProjectId"] = ["Content project does not exist."] });
    try
    {
        var experiment = StyleExperiment.Create(request.Name, request.Hypothesis,
            request.Variants.Select(x => new StyleVariantDraft(x.Name, x.Hook, x.SpeakingStyle,
                x.EditPacing, x.AudioApproach, x.Script, x.CreativeNotes)), DateTimeOffset.UtcNow, request.ContentProjectId, request.Topic);
        db.StyleExperiments.Add(experiment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/style-experiments/{experiment.Id}", new { experiment.Id, experiment.Name, status = experiment.Status.ToString(), variantCount = experiment.Variants.Count });
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["experiment"] = [ex.Message] }); }
});
styleLab.MapPost("/{id:guid}/variants/{variantId:guid}/ready", async (Guid id, Guid variantId, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeStyleVariant(id, variantId, db, ct, variant => variant.MarkReady()));
styleLab.MapPost("/{id:guid}/start", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeStyleExperiment(id, db, ct, (experiment, now) => experiment.Start(now)));
styleLab.MapPost("/{id:guid}/variants/{variantId:guid}/publish", async (Guid id, Guid variantId, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeStyleVariant(id, variantId, db, ct, variant => variant.MarkPublished(DateTimeOffset.UtcNow)));
styleLab.MapPost("/{id:guid}/variants/{variantId:guid}/measure", async (Guid id, Guid variantId, RecordStyleVariantMetricsRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var experiment = await db.StyleExperiments.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (experiment is null) return Results.NotFound();
    if (experiment.Status != StyleExperimentStatus.Running)
        return Results.Conflict(new { error = "Metrics can only be appended while the experiment is Running; evaluated results are immutable." });
    var variant = experiment.Variants.SingleOrDefault(x => x.Id == variantId);
    if (variant is null) return Results.NotFound();
    var observedAt = DateTimeOffset.UtcNow;
    try
    {
        variant.RecordMetrics(request.Views, request.Likes, request.Comments, request.Shares, request.Saves,
            request.FollowsGained, request.ProductClicks, request.Orders, request.AverageWatchSeconds,
            request.VideoDurationSeconds, observedAt);
        db.StyleVariantMetricObservations.Add(StyleVariantMetricObservation.Capture(variant, observedAt));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
    catch (ArgumentOutOfRangeException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["metrics"] = [ex.Message] }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
styleLab.MapGet("/{id:guid}/observations", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!await db.StyleExperiments.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
    var observations = await db.StyleVariantMetricObservations.AsNoTracking()
        .Where(x => db.StyleExperimentVariants.Where(v => v.StyleExperimentId == id)
            .Select(v => v.Id).Contains(x.StyleExperimentVariantId))
        .OrderBy(x => x.ObservedAt)
        .Select(x => new StyleVariantMetricObservationResponse(x.Id, x.StyleExperimentVariantId, x.ObservedAt,
            x.Views, x.Likes, x.Comments, x.Shares, x.Saves, x.FollowsGained, x.ProductClicks, x.Orders,
            x.AverageWatchSeconds, x.VideoDurationSeconds, x.EngagementRate, x.FollowRate,
            x.ProductClickRate, x.OrderRate, x.WatchRetentionRate))
        .ToArrayAsync(ct);
    return Results.Ok(observations);
});

styleLab.MapPost("/{id:guid}/evaluate", async (Guid id, EvaluateStyleExperimentRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
    await ChangeStyleExperiment(id, db, ct, (experiment, now) => experiment.Evaluate(request.WinningVariantId, now)));


// Creator style profiles accumulate confirmed winning patterns as traceable evidence.
var styleProfiles = app.MapGroup("/api/v1/style-profiles").RequireAuthorization();
styleProfiles.MapGet("/{id:guid}/learning-analysis", async (Guid id, string? topic, long? minimumViewsPerVariant, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!await db.CreatorStyleProfiles.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
    var minimumViews = minimumViewsPerVariant ?? 100;
    if (minimumViews < 0 || minimumViews > 1000000000)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["minimumViewsPerVariant"] = ["Must be between 0 and 1,000,000,000."] });
    if (topic?.Trim().Length > 200)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["topic"] = ["Topic cannot exceed 200 characters."] });
    var experiments = await db.StyleExperiments.AsNoTracking().Include(x => x.Variants)
        .Where(x => x.Status == StyleExperimentStatus.Evaluated).ToArrayAsync(ct);
    return Results.Ok(StyleLearningEngine.Analyze(experiments, topic, minimumViews));
});
styleProfiles.MapGet("/", async (int? skip, int? take, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var page = Math.Max(0, skip ?? 0);
    var size = take ?? 50;
    if (size is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Must be between 1 and 100."] });
    var items = await db.CreatorStyleProfiles.AsNoTracking().OrderBy(x => x.DisplayName).Skip(page).Take(size)
        .Select(x => new CreatorStyleProfileListResponse(x.Id, x.ProfileKey, x.DisplayName, x.Positioning,
            x.StyleSummary, x.Evidence.Count, x.CreatedAt, x.UpdatedAt)).ToArrayAsync(ct);
    return Results.Ok(items);
});
styleProfiles.MapPost("/", async (CreateCreatorStyleProfileRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (await db.CreatorStyleProfiles.AnyAsync(x => x.ProfileKey == request.ProfileKey, ct))
        return Results.Conflict(new { error = "A style profile already exists for this profile key." });
    try
    {
        var profile = CreatorStyleProfile.Create(request.ProfileKey, request.DisplayName, request.Positioning, DateTimeOffset.UtcNow);
        db.CreatorStyleProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/style-profiles/{profile.Id}",
            new CreatorStyleProfileListResponse(profile.Id, profile.ProfileKey, profile.DisplayName,
                profile.Positioning, profile.StyleSummary, 0, profile.CreatedAt, profile.UpdatedAt));
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["profile"] = [ex.Message] }); }
});
styleProfiles.MapGet("/{id:guid}", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var profile = await db.CreatorStyleProfiles.AsNoTracking().Include(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    return Results.Ok(new CreatorStyleProfileResponse(profile.Id, profile.ProfileKey, profile.DisplayName,
        profile.Positioning, profile.StyleSummary, profile.Evidence.OrderByDescending(x => x.LearnedAt)
            .Select(ToStyleEvidenceResponse).ToArray(), profile.CreatedAt, profile.UpdatedAt));
});
styleProfiles.MapPut("/{id:guid}/guidance", async (Guid id, UpdateCreatorStyleProfileRequest request, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var profile = await db.CreatorStyleProfiles.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    try
    {
        profile.UpdateGuidance(request.DisplayName, request.Positioning, request.StyleSummary, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["profile"] = [ex.Message] }); }
});
styleProfiles.MapPost("/{id:guid}/learn/{experimentId:guid}", async (Guid id, Guid experimentId, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var profile = await db.CreatorStyleProfiles.Include(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    var experiment = await db.StyleExperiments.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == experimentId, ct);
    if (experiment is null) return Results.NotFound();
    try
    {
        var evidence = profile.LearnFrom(experiment, DateTimeOffset.UtcNow);
        db.CreatorStyleEvidence.Add(evidence);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/style-profiles/{id}", ToStyleEvidenceResponse(evidence));
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});


// Learning Memory persists AI/statistical drafts and requires an explicit review before profile activation.
styleProfiles.MapGet("/{id:guid}/versions", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!await db.CreatorStyleProfiles.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
    var versions = await db.CreatorStyleProfileVersions.AsNoTracking()
        .Where(x => x.CreatorStyleProfileId == id).OrderByDescending(x => x.VersionNumber)
        .Select(x => new CreatorStyleProfileVersionResponse(x.Id, x.VersionNumber, x.ApprovedProposalId,
            x.DisplayName, x.Positioning, x.StyleSummary, x.CreatedAt, x.CreatedBy)).ToArrayAsync(ct);
    return Results.Ok(versions);
});

styleProfiles.MapGet("/{id:guid}/learning-proposals", async (Guid id, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    if (!await db.CreatorStyleProfiles.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
    var proposals = await db.StyleLearningProposals.AsNoTracking()
        .Where(x => x.CreatorStyleProfileId == id).OrderByDescending(x => x.CreatedAt)
        .Select(x => new StyleLearningProposalResponse(x.Id, x.CreatorStyleProfileId, x.ProposedSummary,
            x.EvidenceExperimentIdsJson, x.AlgorithmVersion, x.Status.ToString(), x.ReviewReason,
            x.ReviewerId, x.CreatedAt, x.ReviewedAt)).ToArrayAsync(ct);
    return Results.Ok(proposals);
});

styleProfiles.MapPost("/{id:guid}/learning-proposals", async (Guid id, CreateStyleLearningProposalRequest request,
    CreatorFlowDbContext? db, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var profile = await db.CreatorStyleProfiles.AsNoTracking().Include(x => x.Evidence)
        .SingleOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    if (profile.Evidence.Select(x => x.StyleExperimentId).Distinct().Count() < 2)
        return Results.Conflict(new { error = "At least two imported evaluated experiments are required." });
    try
    {
        var evidence = profile.Evidence.OrderBy(x => x.LearnedAt).Select(x => new
        {
            x.StyleExperimentId, x.WinningVariantId, x.ExperimentName, x.VariantName,
            x.Hook, x.SpeakingStyle, x.EditPacing, x.AudioApproach, x.Views, x.Likes,
            x.Comments, x.Shares, x.Saves, x.FollowsGained, x.ProductClicks, x.Orders,
            x.AverageWatchSeconds, x.VideoDurationSeconds, x.LearnedAt
        }).ToArray();
        var idsJson = JsonSerializer.Serialize(evidence.Select(x => x.StyleExperimentId).Distinct());
        var snapshotJson = JsonSerializer.Serialize(evidence);
        var proposal = StyleLearningProposal.Create(id, request.ProposedSummary, idsJson,
            snapshotJson, request.AlgorithmVersion, DateTimeOffset.UtcNow);
        db.StyleLearningProposals.Add(proposal);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/style-profiles/{id}/learning-proposals/{proposal.Id}",
            new StyleLearningProposalResponse(proposal.Id, id, proposal.ProposedSummary,
                proposal.EvidenceExperimentIdsJson, proposal.AlgorithmVersion, proposal.Status.ToString(),
                null, null, proposal.CreatedAt, null));
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["proposal"] = [ex.Message] });
    }
});

styleProfiles.MapPost("/{id:guid}/learning-proposals/{proposalId:guid}/approve", async (Guid id,
    Guid proposalId, ReviewStyleLearningProposalRequest request, HttpContext httpContext, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    var reviewerId = VerifiedReviewerIdentity.Resolve(httpContext.User);
    if (reviewerId is null) return Results.Forbid();
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var profile = await db.CreatorStyleProfiles.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    var proposal = await db.StyleLearningProposals.SingleOrDefaultAsync(
        x => x.Id == proposalId && x.CreatorStyleProfileId == id, ct);
    if (proposal is null) return Results.NotFound();
    try
    {
        var now = DateTimeOffset.UtcNow;
        proposal.Approve(reviewerId, request.Reason, now);
        profile.UpdateGuidance(profile.DisplayName, profile.Positioning, proposal.ProposedSummary, now);
        var nextVersion = (await db.CreatorStyleProfileVersions.Where(x => x.CreatorStyleProfileId == id)
            .Select(x => (int?)x.VersionNumber).MaxAsync(ct) ?? 0) + 1;
        db.CreatorStyleProfileVersions.Add(CreatorStyleProfileVersion.Capture(
            profile, nextVersion, proposal.Id, reviewerId, now));
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { proposalId, status = proposal.Status.ToString(), version = nextVersion, profile.StyleSummary });
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["review"] = [ex.Message] }); }
});

styleProfiles.MapPost("/{id:guid}/learning-proposals/{proposalId:guid}/reject", async (Guid id,
    Guid proposalId, ReviewStyleLearningProposalRequest request, HttpContext httpContext, CreatorFlowDbContext? db, CancellationToken ct) =>
{
    var reviewerId = VerifiedReviewerIdentity.Resolve(httpContext.User);
    if (reviewerId is null) return Results.Forbid();
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var proposal = await db.StyleLearningProposals.SingleOrDefaultAsync(
        x => x.Id == proposalId && x.CreatorStyleProfileId == id, ct);
    if (proposal is null) return Results.NotFound();
    try
    {
        proposal.Reject(reviewerId, request.Reason, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { proposalId, status = proposal.Status.ToString() });
    }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["review"] = [ex.Message] }); }
});

// AI requests are proxied server-to-server; provider credentials never reach the browser.
var ai = app.MapGroup("/api/v1/ai").RequireAuthorization();
ai.MapPost("/analyze", async (AiAnalyzeRequest request, CreatorFlowAiClient aiClient, CancellationToken ct) =>
    await aiClient.ForwardAsync("/v1/analyze", request, ct));
ai.MapPost("/style-lab/generate-variants", async (AiGenerateVariantsRequest request, CreatorFlowAiClient aiClient, CancellationToken ct) =>
    await aiClient.ForwardAsync("/v1/style-lab/generate-variants", request, ct));


styleProfiles.MapPost("/{id:guid}/ai-learning-synthesis", async (Guid id, CreatorFlowDbContext? db,
    CreatorFlowAiClient aiClient, CancellationToken ct) =>
{
    if (db is null) return (IResult)Results.Problem("SQL Server is not configured.", statusCode: 503);
    var profile = await db.CreatorStyleProfiles.AsNoTracking().Include(x => x.Evidence)
        .SingleOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(profile.Positioning))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["positioning"] = ["Set creator positioning before AI learning synthesis."] });

    var evidenceExperimentIds = profile.Evidence.Select(x => x.StyleExperimentId).Distinct().ToArray();
    var experiments = await db.StyleExperiments.AsNoTracking().Include(x => x.Variants)
        .Where(x => evidenceExperimentIds.Contains(x.Id) && x.Status == StyleExperimentStatus.Evaluated)
        .ToArrayAsync(ct);
    var evidence = new List<AiLearningEvidence>();
    foreach (var experiment in experiments)
    {
        if (!experiment.WinningVariantId.HasValue) continue;
        var winner = experiment.Variants.SingleOrDefault(x => x.Id == experiment.WinningVariantId.Value);
        if (winner is null || winner.Status != StyleVariantStatus.Measured) continue;
        var losers = experiment.Variants.Where(x => x.Id != winner.Id).ToArray();
        var winningFactors = new[]
        {
            $"hook:{winner.Hook}", $"speaking:{winner.SpeakingStyle}",
            $"pacing:{winner.EditPacing}", $"audio:{winner.AudioApproach}"
        };
        var losingFactors = losers.SelectMany(x => new[]
        {
            $"hook:{x.Hook}", $"speaking:{x.SpeakingStyle}", $"pacing:{x.EditPacing}", $"audio:{x.AudioApproach}"
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var metrics = new Dictionary<string, double>
        {
            ["views"] = winner.Views, ["likes"] = winner.Likes, ["comments"] = winner.Comments,
            ["shares"] = winner.Shares, ["saves"] = winner.Saves, ["followsGained"] = winner.FollowsGained,
            ["productClicks"] = winner.ProductClicks, ["orders"] = winner.Orders
        };
        if (winner.AverageWatchSeconds.HasValue) metrics["averageWatchSeconds"] = (double)winner.AverageWatchSeconds.Value;
        if (winner.VideoDurationSeconds.HasValue) metrics["videoDurationSeconds"] = (double)winner.VideoDurationSeconds.Value;
        evidence.Add(new AiLearningEvidence(experiment.Id.ToString(), experiment.Topic,
            winner.Id.ToString(), winningFactors, losingFactors, metrics));
    }
    if (evidence.Count < 2)
        return Results.Conflict(new { error = "At least two evaluated experiments must be imported into this profile before AI synthesis." });

    return await aiClient.ForwardAsync("/v1/learning/synthesize",
        new AiSynthesizeLearningRequest(profile.ProfileKey, profile.Positioning, profile.StyleSummary, evidence), ct);
});

app.MapTenantAccountEndpoints();
app.Run();

static async Task<IResult> ChangeVideoStatus(Guid id, CreatorFlowDbContext? db, CancellationToken ct, Action<DouyinVideoCandidate, DateTimeOffset> transition)
{
    if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
    var video = await db.DouyinVideoCandidates.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (video is null) return Results.NotFound();
    try { transition(video, DateTimeOffset.UtcNow); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
}


static async Task<IResult> ChangeStyleExperiment(Guid id, CreatorFlowDbContext? db, CancellationToken ct, Action<StyleExperiment, DateTimeOffset> transition)
{
    if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
    var experiment = await db.StyleExperiments.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (experiment is null) return Results.NotFound();
    try { transition(experiment, DateTimeOffset.UtcNow); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
}

static async Task<IResult> ChangeStyleVariant(Guid id, Guid variantId, CreatorFlowDbContext? db, CancellationToken ct, Action<StyleExperimentVariant> transition)
{
    if (db is null) return Results.Problem("SQL Server is not configured.", statusCode: 503);
    var experiment = await db.StyleExperiments.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == id, ct);
    if (experiment is null) return Results.NotFound();
    var variant = experiment.Variants.SingleOrDefault(x => x.Id == variantId);
    if (variant is null) return Results.NotFound();
    try { transition(variant); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    catch (ArgumentOutOfRangeException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["metrics"] = [ex.Message] }); }
}


static CreatorStyleEvidenceResponse ToStyleEvidenceResponse(CreatorStyleEvidence x) =>
    new(x.Id, x.StyleExperimentId, x.WinningVariantId, x.ExperimentName, x.VariantName, x.Hook,
        x.SpeakingStyle, x.EditPacing, x.AudioApproach, x.Views, x.Likes, x.Comments, x.Shares,
        x.Saves, x.FollowsGained, x.ProductClicks, x.Orders, x.AverageWatchSeconds,
        x.VideoDurationSeconds, x.EngagementRate, x.LearnedAt);

public sealed record CreatorStyleProfileVersionResponse(Guid Id, int VersionNumber, Guid? ApprovedProposalId,
    string DisplayName, string Positioning, string StyleSummary, DateTimeOffset CreatedAt, string CreatedBy);
public sealed record CreateStyleLearningProposalRequest(string ProposedSummary, string AlgorithmVersion);
public sealed record ReviewStyleLearningProposalRequest(string? Reason);
public sealed record StyleLearningProposalResponse(Guid Id, Guid CreatorStyleProfileId, string ProposedSummary,
    string EvidenceExperimentIdsJson, string AlgorithmVersion, string Status, string? ReviewReason,
    string? ReviewerId, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record CreateCreatorStyleProfileRequest(string ProfileKey, string DisplayName, string? Positioning);
public sealed record UpdateCreatorStyleProfileRequest(string DisplayName, string? Positioning, string? StyleSummary);
public sealed record CreatorStyleProfileListResponse(Guid Id, string ProfileKey, string DisplayName,
    string Positioning, string StyleSummary, int EvidenceCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreatorStyleProfileResponse(Guid Id, string ProfileKey, string DisplayName,
    string Positioning, string StyleSummary, CreatorStyleEvidenceResponse[] Evidence,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreatorStyleEvidenceResponse(Guid Id, Guid StyleExperimentId, Guid WinningVariantId,
    string ExperimentName, string VariantName, string Hook, string SpeakingStyle, string EditPacing,
    string AudioApproach, long Views, long Likes, long Comments, long Shares, long Saves,
    long FollowsGained, long ProductClicks, long Orders, decimal? AverageWatchSeconds,
    decimal? VideoDurationSeconds, decimal? EngagementRate, DateTimeOffset LearnedAt);

public sealed record CreateContentProjectRequest(string Title);
public sealed record CreateVietnamTrendRequest(string Kind, string Platform, string ExternalId, string Title,
    string CreatorName, string SourceUrl, string? Topic, string? SoundName, string? SoundArtist,
    long Views, long Likes, long Comments, long Shares, long Saves, DateTimeOffset? PublishedAt);
public sealed record RecordVietnamTrendObservationRequest(long Views, long Likes, long Comments, long Shares, long Saves);
public sealed record VietnamTrendResponse(Guid Id, string Kind, string Platform, string ExternalId, string Title,
    string CreatorName, string SourceUrl, string? Topic, string? SoundName, string? SoundArtist,
    long Views, long Likes, long Comments, long Shares, long Saves, DateTimeOffset? PublishedAt, DateTimeOffset ObservedAt);
public sealed record CreateStyleVariantRequest(string Name, string Hook, string SpeakingStyle, string EditPacing,
    string AudioApproach, string Script, string? CreativeNotes);
public sealed record CreateStyleExperimentRequest(string Name, string Hypothesis, Guid? ContentProjectId, List<CreateStyleVariantRequest> Variants, string? Topic = null);
public sealed record RecordStyleVariantMetricsRequest(long Views, long Likes, long Comments, long Shares, long Saves,
    long FollowsGained, long ProductClicks, long Orders, decimal? AverageWatchSeconds, decimal? VideoDurationSeconds);
public sealed record EvaluateStyleExperimentRequest(Guid WinningVariantId);
public sealed record StyleExperimentResponse(Guid Id, Guid? ContentProjectId, string Name, string Hypothesis,
    string? Topic, string Status, Guid? WinningVariantId, StyleVariantResponse[] Variants);
public sealed record StyleVariantResponse(Guid Id, string Name, string Hook, string SpeakingStyle, string EditPacing,
    string AudioApproach, string Script, string Status, long Views, long Likes, long Comments, long Shares, long Saves,
    long FollowsGained, long ProductClicks, long Orders, decimal? AverageWatchSeconds, decimal? VideoDurationSeconds,
    decimal? EngagementRate, DateTimeOffset? PublishedAt, DateTimeOffset? MeasuredAt);
public sealed record CreateTopicRequest(string Name, string? Keywords);
public sealed record CreateTopicDiscoveryPlanRequest(string? ProductContext, string? AudienceContext, int? MaxQueries);
public sealed record AiDiscoveryQueryPlanRequest(string Topic, string[] Keywords, string TargetMarket, string? ProductContext, string? AudienceContext, int MaxQueries);
public sealed record UpdateTopicRequest(string Name, string? Keywords);
public sealed record TopicResponse(Guid Id, string Name, string Keywords, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record CreateChannelRequest(string DouyinSecUserId, string DisplayName, string ProfileUrl);
public sealed record ChannelResponse(Guid Id, string DouyinSecUserId, string DisplayName, string ProfileUrl, bool IsFollowed, DateTimeOffset? FollowedAt, DateTimeOffset? LastCheckedAt);
public sealed record CreateVideoCandidateRequest(string DouyinVideoId, Guid? SourceChannelId, Guid? ContentTopicId, string SourceUrl, string? Title, string DiscoveryMethod, DateTimeOffset? PublishedAt);
public sealed record VideoCandidateResponse(Guid Id, string DouyinVideoId, Guid? SourceChannelId, Guid? ContentTopicId, string SourceUrl, string Title, string DiscoveryMethod, string Status, DateTimeOffset? PublishedAt, DateTimeOffset DiscoveredAt);

public sealed record CreateSharedLinkRequest(string SharedUrl);
public sealed record ReviewSharedLinkRequest(string? Note);
public sealed record SharedLinkInboxResponse(Guid Id, string Platform, string SharedUrl,
    DateTimeOffset SharedAt, string Status, string? ReviewNote, Guid? LinkedChannelId);

public partial class Program;

public sealed record AiLearningEvidence(string ExperimentId, string? Topic, string WinningVariantId,
    string[] WinningFactors, string[] LosingFactors, Dictionary<string, double> Metrics);
public sealed record AiSynthesizeLearningRequest(string ProfileKey, string CreatorPositioning,
    string CurrentStyleSummary, List<AiLearningEvidence> Evidence);

public sealed record StyleVariantMetricObservationResponse(Guid Id, Guid StyleExperimentVariantId,
    DateTimeOffset ObservedAt, long Views, long Likes, long Comments, long Shares, long Saves,
    long FollowsGained, long ProductClicks, long Orders, decimal? AverageWatchSeconds,
    decimal? VideoDurationSeconds, decimal? EngagementRate, decimal? FollowRate,
    decimal? ProductClickRate, decimal? OrderRate, decimal? WatchRetentionRate);

public sealed record CreateTrendSelectionFeedbackRequest(string Decision, string ReasonCode, string? ReasonNote);
public sealed record CreateTrendCandidateOutcomeObservationRequest(string OutcomeKind, string? OutcomeValue,
    string MetricsSnapshotJson, string Source);
public sealed record TrendCandidateOutcomeObservationResponse(Guid Id, Guid FeedbackId, string OutcomeKind,
    string? OutcomeValue, string MetricsSnapshotJson, string Source, DateTimeOffset ObservedAt);
public sealed record TrendSelectionFeedbackResponse(Guid Id, Guid DouyinVideoCandidateId, string Decision, string ReasonCode, string? ReasonNote, string CandidateSnapshotJson, string FeatureSnapshotJson, DateTimeOffset ReviewedAt);
