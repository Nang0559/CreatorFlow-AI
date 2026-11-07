using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class TenantRolePipelineIntegrationTests
{
    [Fact]
    public async Task Editor_cannot_approve_learning_proposal_through_real_api_pipeline()
    {
        using var factory = new CreatorFlowApiFactory("Editor");
        await factory.SeedAsync(_ => Task.CompletedTask);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", CreatorFlowApiFactory.ApiKey);

        var profileId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync(
            $"/api/v1/style-profiles/{profileId}/learning-proposals/{proposalId}/approve", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Editor_cannot_read_connector_credentials_through_real_api_pipeline()
    {
        using var factory = new CreatorFlowApiFactory("Editor");
        await factory.SeedAsync(_ => Task.CompletedTask);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", CreatorFlowApiFactory.ApiKey);

        var response = await client.GetAsync("/api/v1/discovery/connectors/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
