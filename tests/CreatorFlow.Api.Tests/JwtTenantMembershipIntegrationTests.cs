using System.Net;
using System.Net.Http.Headers;
using CreatorFlow.Domain.Identity;
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
