using System.Net;
using System.Net.Http.Headers;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Domain.Discovery;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class JwtTenantMembershipIntegrationTests
{
    private const string Subject = "keycloak-subject-creatorflow-ci";

    [Fact]
    public async Task Signed_jwt_with_active_membership_resolves_workspace_and_reaches_endpoint()
    {
        using var factory = new CreatorFlowApiFactory();
        await factory.SeedAsync((db, _) =>
        {
            db.TenantMemberships.Add(TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Editor,
                DateTimeOffset.UtcNow,
                "creatorflow-ci@example.invalid"));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        var response = await client.GetAsync("/api/v1/topics/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Jwt_membership_role_not_bootstrap_configuration_controls_authorization()
    {
        // The factory's API-key bootstrap role defaults to Owner. This request uses JWT,
        // so the Editor role must come from the active SQL-model membership row instead.
        using var factory = new CreatorFlowApiFactory("Owner");
        await factory.SeedAsync((db, _) =>
        {
            db.TenantMemberships.Add(TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Editor,
                DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        var response = await client.GetAsync("/api/v1/discovery/connectors/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signed_jwt_cannot_select_a_workspace_without_active_membership()
    {
        using var factory = new CreatorFlowApiFactory();
        var foreignTenantId = Guid.NewGuid();
        await factory.SeedAsync(async (db, _) =>
        {
            var foreignTenant = Tenant.Create("Foreign workspace", $"foreign-{foreignTenantId:N}", DateTimeOffset.UtcNow);
            db.Entry(foreignTenant).Property(x => x.Id).CurrentValue = foreignTenantId;
            db.Tenants.Add(foreignTenant);
            db.TenantMemberships.Add(TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Owner,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        client.DefaultRequestHeaders.Remove(TenantContextHeader);
        client.DefaultRequestHeaders.Add(TenantContextHeader, foreignTenantId.ToString());
        var response = await client.GetAsync("/api/v1/topics/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Suspended_membership_cannot_resolve_workspace()
    {
        using var factory = new CreatorFlowApiFactory();
        await factory.SeedAsync((db, _) =>
        {
            var membership = TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Owner,
                DateTimeOffset.UtcNow);
            membership.Suspend(DateTimeOffset.UtcNow);
            db.TenantMemberships.Add(membership);
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        var response = await client.GetAsync("/api/v1/topics/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Jwt_membership_scopes_topic_list_to_selected_workspace()
    {
        using var factory = new CreatorFlowApiFactory();
        var foreignTenantId = Guid.NewGuid();
        await factory.SeedAsync(async (db, tenantContext) =>
        {
            db.TenantMemberships.Add(TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Owner,
                DateTimeOffset.UtcNow));
            var foreignTenant = Tenant.Create("Foreign workspace", $"foreign-{foreignTenantId:N}", DateTimeOffset.UtcNow);
            db.Entry(foreignTenant).Property(x => x.Id).CurrentValue = foreignTenantId;
            db.Tenants.Add(foreignTenant);
            tenantContext.Set(foreignTenantId, "Owner");
            db.ContentTopics.Add(ContentTopic.Create("Foreign-only topic", "foreign", DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();

            tenantContext.Set(CreatorFlowApiFactory.LegacyTenantId, "Owner");
            db.ContentTopics.Add(ContentTopic.Create("Current workspace topic", "current", DateTimeOffset.UtcNow));
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        var response = await client.GetAsync("/api/v1/topics/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = payload.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("Current workspace topic", names);
        Assert.DoesNotContain("Foreign-only topic", names);
    }

    [Fact]
    public async Task Owner_membership_satisfies_endpoint_scoped_connector_settings_policy()
    {
        using var factory = new CreatorFlowApiFactory();
        await factory.SeedAsync((db, _) =>
        {
            db.TenantMemberships.Add(TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Owner,
                DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        var response = await client.GetAsync("/api/v1/discovery/connectors/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Jwt_membership_does_not_read_foreign_workspace_connector_credentials()
    {
        using var factory = new CreatorFlowApiFactory();
        var foreignTenantId = Guid.NewGuid();
        await factory.SeedAsync(async (db, tenantContext) =>
        {
            db.TenantMemberships.Add(TenantMembership.Create(
                CreatorFlowApiFactory.LegacyTenantId,
                CreatorFlowApiFactory.TestJwtIssuer,
                Subject,
                TenantRole.Owner,
                DateTimeOffset.UtcNow));
            var foreignTenant = Tenant.Create("Foreign workspace", $"foreign-{foreignTenantId:N}", DateTimeOffset.UtcNow);
            db.Entry(foreignTenant).Property(x => x.Id).CurrentValue = foreignTenantId;
            db.Tenants.Add(foreignTenant);
            tenantContext.Set(foreignTenantId, "Owner");
            db.DiscoveryConnectorConfigurations.Add(new DiscoveryConnectorConfiguration
            {
                TenantId = foreignTenantId,
                EncryptedBraveApiKey = "foreign-workspace-ciphertext"
            });
            await db.SaveChangesAsync();
            tenantContext.Set(CreatorFlowApiFactory.LegacyTenantId, "Owner");
        });

        using var client = CreateAuthenticatedClient(factory, Subject);
        var response = await client.GetAsync("/api/v1/discovery/connectors/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(payload.RootElement.GetProperty("hasBraveApiKey").GetBoolean());
        Assert.False(payload.RootElement.GetProperty("isStoredInWorkspace").GetBoolean());
    }

    private static HttpClient CreateAuthenticatedClient(CreatorFlowApiFactory factory, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreatorFlowApiFactory.CreateJwtToken(subject));
        client.DefaultRequestHeaders.Add(TenantContextHeader, CreatorFlowApiFactory.LegacyTenantId.ToString());
        return client;
    }

    private const string TenantContextHeader = "X-CreatorFlow-Tenant";
}
