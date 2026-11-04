using CreatorFlow.Domain.Identity;
using CreatorFlow.Api.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CreatorFlow.Api.Tests;

internal sealed class CreatorFlowApiFactory : WebApplicationFactory<Program>
{
    internal const string ApiKey = "integration-test-api-key-creatorflow-000000000000";
    internal static readonly Guid LegacyTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private readonly string _databaseName = $"creatorflow-api-integration-{Guid.NewGuid():N}";
    private readonly string _bootstrapRole;

    internal CreatorFlowApiFactory(string bootstrapRole = "Owner") => _bootstrapRole = bootstrapRole;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CreatorFlow"] = "Server=localhost;Database=CreatorFlow_QC;Integrated Security=True;TrustServerCertificate=True",
                ["Authentication:ApiKey"] = ApiKey,
                ["Tenant:AllowApiKeyBootstrap"] = "true",
                ["Tenant:BootstrapTenantId"] = LegacyTenantId.ToString(),
                ["Tenant:BootstrapRole"] = _bootstrapRole,
                ["RateLimiting:Enabled"] = "false"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CreatorFlowDbContext>>();
            services.RemoveAll<CreatorFlowDbContext>();
            services.AddDbContext<CreatorFlowDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    internal Task SeedAsync(Func<CreatorFlowDbContext, Task> seed) =>
        SeedAsync((db, _) => seed(db));

    internal async Task SeedAsync(Func<CreatorFlowDbContext, TenantContext, Task> seed)
    {
        using var scope = Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(LegacyTenantId, "Owner");
        var db = scope.ServiceProvider.GetRequiredService<CreatorFlowDbContext>();
        if (!await db.Tenants.AnyAsync(x => x.Id == LegacyTenantId))
        {
            var tenant = Tenant.Create("Legacy workspace", "legacy-workspace", DateTimeOffset.UtcNow);
            db.Entry(tenant).Property(x => x.Id).CurrentValue = LegacyTenantId;
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }
        await seed(db, tenantContext);
        await db.SaveChangesAsync();
    }
}
