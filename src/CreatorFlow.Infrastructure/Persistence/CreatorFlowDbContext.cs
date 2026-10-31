using CreatorFlow.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Infrastructure.Persistence;

public sealed class CreatorFlowDbContext(DbContextOptions<CreatorFlowDbContext> options) : DbContext(options)
{
    public DbSet<ContentProject> ContentProjects => Set<ContentProject>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContentProject>(entity =>
        {
            entity.ToTable("ContentProjects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => new { x.Status, x.UpdatedAt });
        });
    }
}