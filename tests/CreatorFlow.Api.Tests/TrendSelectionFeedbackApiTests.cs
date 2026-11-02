using System.Net;
using System.Net.Http.Json;
using CreatorFlow.Api.Security;
using CreatorFlow.Api.Identity;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Domain.Discovery;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class TrendSelectionFeedbackApiTests
{
    private const string ApiKey = "creatorflow-test-api-key-with-more-than-32-characters";
    private const string FeedbackPath = "/api/v1/douyin/videos/{0}/feedback";

    [Fact]
    public async Task Feedback_post_and_history_round_trip_allows_empty_note()
    {
        using var factory = new CreatorFlowApiFactory();
        var candidateId = await factory.SeedCandidateAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(string.Format(FeedbackPath, candidateId),
            new { decision = "Select", reasonCode = "Hook", reasonNote = "" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("Select", created.RootElement.GetProperty("decision").GetString());
        Assert.Equal("", created.RootElement.GetProperty("reasonNote").GetString());

        var historyResponse = await client.GetAsync(string.Format(FeedbackPath, candidateId));
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var history = await System.Text.Json.JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        var items = history.RootElement.EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("Select", items[0].GetProperty("decision").GetString());
        Assert.Equal("", items[0].GetProperty("reasonNote").GetString());
        using var featureSnapshot = System.Text.Json.JsonDocument.Parse(
            items[0].GetProperty("featureSnapshotJson").GetString()!);
        Assert.Equal("creatorflow.trend-selection", featureSnapshot.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1, featureSnapshot.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(featureSnapshot.RootElement.TryGetProperty("status", out _));
        Assert.False(featureSnapshot.RootElement.TryGetProperty("decision", out _));
    }

    [Fact]
    public async Task Feedback_post_rejects_numeric_undefined_decision()
    {
        using var factory = new CreatorFlowApiFactory();
        var candidateId = await factory.SeedCandidateAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(string.Format(FeedbackPath, candidateId),
            new { decision = "999", reasonCode = "Hook", reasonNote = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Feedback_routes_require_api_key()
    {
        using var factory = new CreatorFlowApiFactory();
        var candidateId = await factory.SeedCandidateAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(string.Format(FeedbackPath, candidateId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Outcome_post_and_history_round_trip()
    {
        using var factory = new CreatorFlowApiFactory();
        var candidateId = await factory.SeedCandidateAsync();
        using var client = factory.CreateAuthenticatedClient();

        var feedbackResponse = await client.PostAsJsonAsync(string.Format(FeedbackPath, candidateId),
            new { decision = "Select", reasonCode = "CommercialPotential", reasonNote = "" });
        Assert.Equal(HttpStatusCode.Created, feedbackResponse.StatusCode);
        using var feedbackJson = await System.Text.Json.JsonDocument.ParseAsync(await feedbackResponse.Content.ReadAsStreamAsync());
        var feedbackId = feedbackJson.RootElement.GetProperty("id").GetGuid();
        var outcomePath = $"/api/v1/douyin/videos/{candidateId}/feedback/{feedbackId}/outcomes";

        var outcomeResponse = await client.PostAsJsonAsync(outcomePath,
            new { outcomeKind = "Published", outcomeValue = "Published via approved flow",
                metricsSnapshotJson = """{"views":1200,"orders":4}""", source = "OfficialAnalyticsApi" });
        Assert.Equal(HttpStatusCode.Created, outcomeResponse.StatusCode);

        var historyResponse = await client.GetAsync(outcomePath);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var history = await System.Text.Json.JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        var items = history.RootElement.EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("Published", items[0].GetProperty("outcomeKind").GetString());
        Assert.Equal("OfficialAnalyticsApi", items[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Outcome_post_rejects_undefined_numeric_kind()
    {
        using var factory = new CreatorFlowApiFactory();
        var candidateId = await factory.SeedCandidateAsync();
        using var client = factory.CreateAuthenticatedClient();
        var feedbackResponse = await client.PostAsJsonAsync(string.Format(FeedbackPath, candidateId),
            new { decision = "Watch", reasonCode = "Uncertain", reasonNote = "" });
        using var feedbackJson = await System.Text.Json.JsonDocument.ParseAsync(await feedbackResponse.Content.ReadAsStreamAsync());
        var feedbackId = feedbackJson.RootElement.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/douyin/videos/{candidateId}/feedback/{feedbackId}/outcomes",
            new { outcomeKind = "999", outcomeValue = (string?)null, metricsSnapshotJson = "{}", source = "Manual" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Feedback_post_returns_not_found_for_unknown_candidate()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(string.Format(FeedbackPath, Guid.NewGuid()),
            new { decision = "Watch", reasonCode = "Uncertain", reasonNote = "" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }


    [Fact]
    public async Task Share_inbox_accepts_and_lists_a_creator_submitted_link()
    {
        using var factory = new CreatorFlowApiFactory();
        await factory.SeedCandidateAsync();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/share-inbox",
            new { sharedUrl = "https://v.douyin.com/AbC123/" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("Douyin", created.RootElement.GetProperty("platform").GetString());
        Assert.Equal("Pending", created.RootElement.GetProperty("status").GetString());

        var historyResponse = await client.GetAsync("/api/v1/share-inbox");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        using var history = await System.Text.Json.JsonDocument.ParseAsync(await historyResponse.Content.ReadAsStreamAsync());
        Assert.Single(history.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Share_inbox_duplicate_submission_is_idempotent()
    {
        using var factory = new CreatorFlowApiFactory();
        await factory.SeedCandidateAsync();
        using var client = factory.CreateAuthenticatedClient();
        var payload = new { sharedUrl = "https://v.douyin.com/AbC123/" };

        var first = await client.PostAsJsonAsync("/api/v1/share-inbox", payload);
        var second = await client.PostAsJsonAsync("/api/v1/share-inbox", payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstJson = await System.Text.Json.JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        using var secondJson = await System.Text.Json.JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
        Assert.Equal(firstJson.RootElement.GetProperty("id").GetGuid(), secondJson.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Share_inbox_rejects_untrusted_domains_and_requires_api_key()
    {
        using var factory = new CreatorFlowApiFactory();
        await factory.SeedCandidateAsync();
        using var authenticated = factory.CreateAuthenticatedClient();
        using var anonymous = factory.CreateClient();

        var rejected = await authenticated.PostAsJsonAsync("/api/v1/share-inbox",
            new { sharedUrl = "https://evil.example/douyin" });
        var unauthorized = await anonymous.GetAsync("/api/v1/share-inbox");

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private sealed class CreatorFlowApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"creatorflow-api-tests-{Guid.NewGuid():N}";
        private static readonly Guid BootstrapTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CreatorFlow", string.Empty);
            builder.UseSetting("Authentication:ApiKey", ApiKey);
            builder.UseSetting("Tenant:AllowApiKeyBootstrap", "true");
            builder.UseSetting("Tenant:BootstrapTenantId", BootstrapTenantId.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<CreatorFlowDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }

        public HttpClient CreateAuthenticatedClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, ApiKey);
            return client;
        }

        public async Task<Guid> SeedCandidateAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CreatorFlowDbContext>();
            var now = DateTimeOffset.UtcNow;
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>() as TenantContext
                ?? throw new InvalidOperationException("Tenant context is not registered.");
            var tenant = Tenant.Create("Legacy workspace", "legacy-workspace", now);
            db.Entry(tenant).Property(x => x.Id).CurrentValue = BootstrapTenantId;
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            tenantContext.Set(BootstrapTenantId, TenantRole.Owner.ToString());
            var candidate = DouyinVideoCandidate.Create("test-video-001", null,
                "https://www.douyin.com/video/test-video-001", "Test candidate",
                VideoDiscoveryMethod.Manual, null, now);
            db.DouyinVideoCandidates.Add(candidate);
            await db.SaveChangesAsync();
            return candidate.Id;
        }
    }

    [Fact]
    public async Task Video_batch_requires_two_or_three_distinct_supported_styles()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/video/render-jobs/batch",
            new { assetId = Guid.NewGuid(), title = "Tiêu đề", hook = "Câu mở đầu",
                callToAction = (string?)null, styleKeys = new[] { "Tự nhiên" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Video_single_job_rejects_unknown_style()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/video/render-jobs",
            new { assetId = Guid.NewGuid(), title = "Tiêu đề", hook = "Câu mở đầu",
                callToAction = (string?)null, styleKey = "Không tồn tại" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
