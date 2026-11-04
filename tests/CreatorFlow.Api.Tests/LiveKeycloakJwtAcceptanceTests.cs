using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using CreatorFlow.Api.Identity;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class LiveKeycloakJwtAcceptanceTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Api_accepts_real_creatorflow_web_token_and_resolves_sql_membership()
    {
        var accessToken = Environment.GetEnvironmentVariable("CREATORFLOW_LIVE_OIDC_TOKEN");
        if (string.IsNullOrWhiteSpace(accessToken))
            return; // The dedicated Keycloak CI job supplies the live token.

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        Assert.Equal("http://localhost:8180/realms/creatorflow", jwt.Issuer);
        var subject = jwt.Subject;
        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.Contains("creatorflow-api", jwt.Audiences);

        using var factory = new LiveKeycloakApiFactory();
        await factory.SeedMembershipAsync(jwt.Issuer, subject!);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Tenant", TenantId.ToString());

        var response = await client.GetAsync("/api/v1/topics/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class LiveKeycloakApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"creatorflow-live-oidc-{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:CreatorFlow"] = "Server=localhost;Database=CreatorFlow_QC;Integrated Security=True;TrustServerCertificate=True",
                    ["Authentication:Oidc:Authority"] = "http://localhost:8180/realms/creatorflow",
                    ["Authentication:Oidc:Audience"] = "creatorflow-api",
                    ["Authentication:ApiKey"] = "",
                    ["Tenant:AllowApiKeyBootstrap"] = "false"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CreatorFlowDbContext>>();
                services.RemoveAll<CreatorFlowDbContext>();
                services.AddDbContext<CreatorFlowDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));
            });
        }

        internal async Task SeedMembershipAsync(string issuer, string subject)
        {
            using var scope = Services.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TenantId, "Owner");
            var db = scope.ServiceProvider.GetRequiredService<CreatorFlowDbContext>();
            var tenant = Tenant.Create("Live OIDC workspace", "live-oidc-workspace", DateTimeOffset.UtcNow);
            db.Entry(tenant).Property(x => x.Id).CurrentValue = TenantId;
            db.Tenants.Add(tenant);
            db.TenantMemberships.Add(TenantMembership.Create(
                TenantId, issuer, subject, TenantRole.Owner, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
    }
}
