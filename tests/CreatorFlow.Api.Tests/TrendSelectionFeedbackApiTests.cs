using System.Net;
using System.Net.Http.Json;
using CreatorFlow.Api.Security;
using CreatorFlow.Domain.Discovery;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        Assert.Equal("{}", items[0].GetProperty("featureSnapshotJson").GetString());
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
    public async Task Feedback_post_returns_not_found_for_unknown_candidate()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = factory.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(string.Format(FeedbackPath, Guid.NewGuid()),
            new { decision = "Watch", reasonCode = "Uncertain", reasonNote = "" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class CreatorFlowApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"creatorflow-api-tests-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CreatorFlow",
                "Server=localhost;Database=CreatorFlowTests;User Id=test;Password=not-a-real-password;TrustServerCertificate=True");
            builder.UseSetting("Authentication:ApiKey", ApiKey);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<CreatorFlowDbContext>();
                services.RemoveAll<DbContextOptions<CreatorFlowDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CreatorFlowDbContext>>();
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
            var candidate = DouyinVideoCandidate.Create("test-video-001", null,
                "https://www.douyin.com/video/test-video-001", "Test candidate",
                VideoDiscoveryMethod.Manual, null, now);
            db.DouyinVideoCandidates.Add(candidate);
            await db.SaveChangesAsync();
            return candidate.Id;
        }
    }
}
