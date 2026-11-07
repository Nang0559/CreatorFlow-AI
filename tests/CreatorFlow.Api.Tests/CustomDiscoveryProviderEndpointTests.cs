using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Xunit;

namespace CreatorFlow.Api.Tests;

/// <summary>
/// Endpoint-level tests for workspace custom discovery providers: validation, secret handling,
/// role policy and tenant isolation. They run on EF InMemory (no SQL Server parity) and do not
/// perform outbound network calls.
/// </summary>
public sealed class CustomDiscoveryProviderEndpointTests
{
    private const string Route = "/api/v1/discovery/connectors/custom-providers";

    private static HttpClient CreateClient(CreatorFlowApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", CreatorFlowApiFactory.ApiKey);
        return client;
    }

    private static object Provider(
        string name = "Example Search",
        string endpoint = "https://search.example.org/api",
        string? headersJson = null,
        string? apiKey = null,
        string? apiKeyHeader = null) => new
        {
            name,
            enabled = true,
            endpoint,
            method = "GET",
            headersJson,
            apiKey,
            apiKeyHeader,
            apiKeyPrefix = (string?)null
        };

    [Theory]
    [InlineData("http://search.example.org/api")]       // not HTTPS
    [InlineData("https://search.example.org:8443/api")] // non-standard port
    [InlineData("https://user:pass@search.example.org/api")]
    [InlineData("https://localhost/api")]
    [InlineData("https://127.0.0.1/api")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.0.0.5/api")]
    [InlineData("https://intranet/api")]
    public async Task Rejects_unsafe_endpoints_when_saving(string endpoint)
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = CreateClient(factory);

        var response = await client.PutAsJsonAsync(Route, new[] { Provider(endpoint: endpoint) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Brave Search")]
    [InlineData("tiktok research api")]
    public async Task Rejects_names_reserved_for_system_sources(string name)
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = CreateClient(factory);

        var response = await client.PutAsJsonAsync(Route, new[] { Provider(name: name) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"Host\":\"internal.example\"}", null)]
    [InlineData("{\"Content-Length\":\"0\"}", null)]
    [InlineData(null, "Host")]
    [InlineData("{\"X-Test\":\"a\\r\\nInjected: yes\"}", null)]
    public async Task Rejects_forbidden_headers_when_saving(string? headersJson, string? apiKeyHeader)
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = CreateClient(factory);

        var response = await client.PutAsJsonAsync(Route,
            new[] { Provider(headersJson: headersJson, apiKey: apiKeyHeader is null ? null : "k", apiKeyHeader: apiKeyHeader) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Saved_secrets_are_never_returned_to_the_client()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = CreateClient(factory);
        const string apiKey = "SUPER-SECRET-PROVIDER-KEY-123";
        const string headerSecret = "HEADER-SECRET-VALUE-456";

        var put = await client.PutAsJsonAsync(Route, new[]
        {
            Provider(headersJson: $"{{\"X-Client-Token\":\"{headerSecret}\"}}", apiKey: apiKey, apiKeyHeader: "X-API-Key")
        });
        var putBody = await put.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.DoesNotContain(apiKey, putBody, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, putBody, StringComparison.Ordinal);

        var get = await client.GetAsync(Route);
        var getBody = await get.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.DoesNotContain(apiKey, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, getBody, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(getBody);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.True(item.GetProperty("hasApiKey").GetBoolean());
        Assert.True(item.GetProperty("hasHeaders").GetBoolean());
    }

    [Fact]
    public async Task Updating_without_resending_secrets_keeps_the_stored_ones()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = CreateClient(factory);

        var created = await client.PutAsJsonAsync(Route, new[]
        {
            Provider(headersJson: "{\"X-Client\":\"creatorflow\"}", apiKey: "stored-key", apiKeyHeader: "X-API-Key")
        });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement[0].GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync(Route, new[]
        {
            new { id, name = "Renamed Search", enabled = true, endpoint = "https://search.example.org/api",
                  method = "GET", apiKeyHeader = "X-API-Key" }
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using var listed = JsonDocument.Parse(await (await client.GetAsync(Route)).Content.ReadAsStringAsync());
        var item = Assert.Single(listed.RootElement.EnumerateArray());
        Assert.Equal("Renamed Search", item.GetProperty("name").GetString());
        Assert.True(item.GetProperty("hasApiKey").GetBoolean());
        Assert.True(item.GetProperty("hasHeaders").GetBoolean());
    }

    [Fact]
    public async Task Rejects_duplicate_provider_names_and_oversized_lists()
    {
        using var factory = new CreatorFlowApiFactory();
        using var client = CreateClient(factory);

        var duplicate = await client.PutAsJsonAsync(Route, new[] { Provider(), Provider(name: "example search") });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var tooMany = Enumerable.Range(0, 21).Select(i => Provider(name: $"Provider {i}")).ToArray();
        var oversized = await client.PutAsJsonAsync(Route, tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    [Fact]
    public async Task Editor_cannot_read_or_change_custom_providers()
    {
        using var factory = new CreatorFlowApiFactory("Editor");
        using var client = CreateClient(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(Route, new[] { Provider() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/discovery/search/provider-test",
                new { providerId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task Provider_test_returns_not_found_for_a_provider_of_another_workspace()
    {
        using var factory = new CreatorFlowApiFactory();
        var foreignTenantId = Guid.NewGuid();
        await factory.SeedAsync(async (db, tenantContext) =>
        {
            var foreign = Tenant.Create("Foreign workspace", $"foreign-{foreignTenantId:N}", DateTimeOffset.UtcNow);
            db.Entry(foreign).Property(x => x.Id).CurrentValue = foreignTenantId;
            db.Tenants.Add(foreign);
            tenantContext.Set(foreignTenantId, "Owner");
            db.DiscoveryConnectorConfigurations.Add(new DiscoveryConnectorConfiguration
            {
                TenantId = foreignTenantId,
                EncryptedCustomProvidersJson = "ciphertext-from-another-workspace"
            });
            await db.SaveChangesAsync();
        });

        using var client = CreateClient(factory);

        var list = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listed = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Empty(listed.RootElement.EnumerateArray());

        var test = await client.PostAsJsonAsync("/api/v1/discovery/search/provider-test",
            new { providerId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, test.StatusCode);
    }
}
