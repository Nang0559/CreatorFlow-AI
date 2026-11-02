using CreatorFlow.Api.Identity;
using CreatorFlow.Domain.Content;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Queries_are_filtered_and_new_rows_are_stamped_for_the_active_tenant()
    {
        var databaseName = $"tenant-isolation-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<CreatorFlowDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await SeedWorkspaceAsync(options, tenantA, "tenant-a", "Workspace A", now);
        await SeedWorkspaceAsync(options, tenantB, "tenant-b", "Workspace B", now);

        var contextA = new TenantContext();
        contextA.Set(tenantA, TenantRole.Owner.ToString());
        using (var dbA = new CreatorFlowDbContext(options, contextA))
        {
            var project = ContentProject.Create("Project A", now);
            dbA.ContentProjects.Add(project);
            await dbA.SaveChangesAsync();
            Assert.Equal(tenantA, dbA.Entry(project).Property<Guid>("TenantId").CurrentValue);
        }

        var contextB = new TenantContext();
        contextB.Set(tenantB, TenantRole.Owner.ToString());
        using (var dbB = new CreatorFlowDbContext(options, contextB))
        {
            var project = ContentProject.Create("Project B", now);
            dbB.ContentProjects.Add(project);
            await dbB.SaveChangesAsync();
            Assert.Equal(tenantB, dbB.Entry(project).Property<Guid>("TenantId").CurrentValue);
        }

        using (var dbA = new CreatorFlowDbContext(options, contextA))
            Assert.Equal(new[] { "Project A" }, await dbA.ContentProjects.Select(x => x.Title).ToArrayAsync());

        using (var dbB = new CreatorFlowDbContext(options, contextB))
            Assert.Equal(new[] { "Project B" }, await dbB.ContentProjects.Select(x => x.Title).ToArrayAsync());
    }

    [Fact]
    public async Task Existing_row_tenant_key_cannot_be_changed()
    {
        var options = new DbContextOptionsBuilder<CreatorFlowDbContext>()
            .UseInMemoryDatabase($"tenant-move-{Guid.NewGuid():N}")
            .Options;
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedWorkspaceAsync(options, tenantA, "tenant-a", "Workspace A", now);
        await SeedWorkspaceAsync(options, tenantB, "tenant-b", "Workspace B", now);

        var contextA = new TenantContext();
        contextA.Set(tenantA, TenantRole.Owner.ToString());
        using var dbA = new CreatorFlowDbContext(options, contextA);
        var project = ContentProject.Create("Protected project", now);
        dbA.ContentProjects.Add(project);
        await dbA.SaveChangesAsync();

        // TenantId is part of every same-tenant alternate key; EF rejects a key move immediately.
        Assert.Throws<InvalidOperationException>(() =>
            dbA.Entry(project).Property<Guid>("TenantId").CurrentValue = tenantB);
        Assert.Equal(tenantA, dbA.Entry(project).Property<Guid>("TenantId").CurrentValue);
    }

    private static async Task SeedWorkspaceAsync(DbContextOptions<CreatorFlowDbContext> options,
        Guid tenantId, string slug, string name, DateTimeOffset now)
    {
        using var db = new CreatorFlowDbContext(options, new TenantContext());
        var tenant = Tenant.Create(name, slug, now);
        db.Entry(tenant).Property(x => x.Id).CurrentValue = tenantId;
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
    }
}
