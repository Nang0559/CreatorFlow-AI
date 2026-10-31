using CreatorFlow.Domain.Content;
using CreatorFlow.Domain.Discovery;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Infrastructure.Persistence;

public sealed class CreatorFlowDbContext(DbContextOptions<CreatorFlowDbContext> options) : DbContext(options)
{
    public DbSet<ContentProject> ContentProjects => Set<ContentProject>();
    public DbSet<ContentTopic> ContentTopics => Set<ContentTopic>();
    public DbSet<DouyinSourceChannel> DouyinSourceChannels => Set<DouyinSourceChannel>();
    public DbSet<DouyinVideoCandidate> DouyinVideoCandidates => Set<DouyinVideoCandidate>();

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

        modelBuilder.Entity<ContentTopic>(entity =>
        {
            entity.ToTable("ContentTopics");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Keywords).HasMaxLength(4000).IsRequired();
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<DouyinSourceChannel>(entity =>
        {
            entity.ToTable("DouyinSourceChannels");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DouyinSecUserId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ProfileUrl).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => x.DouyinSecUserId).IsUnique();
            entity.HasIndex(x => new { x.IsFollowed, x.LastCheckedAt });
        });

        modelBuilder.Entity<DouyinVideoCandidate>(entity =>
        {
            entity.ToTable("DouyinVideoCandidates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DouyinVideoId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.SourceUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(500).IsRequired();
            entity.Property(x => x.DiscoveryMethod).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.DouyinVideoId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.DiscoveredAt });
            entity.HasOne<DouyinSourceChannel>().WithMany().HasForeignKey(x => x.SourceChannelId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
