using CreatorFlow.Domain.Content;
using CreatorFlow.Domain.Discovery;
using CreatorFlow.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Infrastructure.Persistence;

public sealed class CreatorFlowDbContext(DbContextOptions<CreatorFlowDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<ContentProject> ContentProjects => Set<ContentProject>();
    public DbSet<ContentTopic> ContentTopics => Set<ContentTopic>();
    public DbSet<DouyinSourceChannel> DouyinSourceChannels => Set<DouyinSourceChannel>();
    public DbSet<DouyinVideoCandidate> DouyinVideoCandidates => Set<DouyinVideoCandidate>();
    public DbSet<TrendSelectionFeedback> TrendSelectionFeedback => Set<TrendSelectionFeedback>();
    public DbSet<TrendCandidateOutcomeObservation> TrendCandidateOutcomeObservations => Set<TrendCandidateOutcomeObservation>();
    public DbSet<VietnamTrendSignal> VietnamTrendSignals => Set<VietnamTrendSignal>();
    public DbSet<VietnamTrendMetricSnapshot> VietnamTrendMetricSnapshots => Set<VietnamTrendMetricSnapshot>();
    public DbSet<StyleExperiment> StyleExperiments => Set<StyleExperiment>();
    public DbSet<StyleExperimentVariant> StyleExperimentVariants => Set<StyleExperimentVariant>();
    public DbSet<CreatorStyleProfile> CreatorStyleProfiles => Set<CreatorStyleProfile>();
    public DbSet<CreatorStyleEvidence> CreatorStyleEvidence => Set<CreatorStyleEvidence>();
    public DbSet<StyleVariantMetricObservation> StyleVariantMetricObservations => Set<StyleVariantMetricObservation>();
    public DbSet<StyleLearningProposal> StyleLearningProposals => Set<StyleLearningProposal>();
    public DbSet<CreatorStyleProfileVersion> CreatorStyleProfileVersions => Set<CreatorStyleProfileVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.ToTable("TenantMemberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdentityIssuer).HasMaxLength(500).IsRequired();
            entity.Property(x => x.SubjectId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.TenantId, x.IdentityIssuer, x.SubjectId }).IsUnique();
            entity.HasIndex(x => new { x.IdentityIssuer, x.SubjectId, x.Status });
        });

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

        modelBuilder.Entity<VietnamTrendSignal>(entity =>
        {
            entity.ToTable("VietnamTrendSignals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.Platform).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatorName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SourceUrl).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Topic).HasMaxLength(200);
            entity.Property(x => x.SoundName).HasMaxLength(300);
            entity.Property(x => x.SoundArtist).HasMaxLength(200);
            entity.HasIndex(x => new { x.Platform, x.Kind, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.Kind, x.ObservedAt });
        });

        modelBuilder.Entity<VietnamTrendMetricSnapshot>(entity =>
        {
            entity.ToTable("VietnamTrendMetricSnapshots");
            entity.HasKey(x => x.Id);
            entity.HasOne<VietnamTrendSignal>().WithMany().HasForeignKey(x => x.TrendSignalId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.TrendSignalId, x.ObservedAt }).IsUnique();
        });

        modelBuilder.Entity<CreatorStyleProfile>(entity =>
        {
            entity.ToTable("CreatorStyleProfiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProfileKey).HasMaxLength(160).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Positioning).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.StyleSummary).HasMaxLength(4000).IsRequired();
            entity.HasIndex(x => x.ProfileKey).IsUnique();
            entity.HasMany(x => x.Evidence).WithOne().HasForeignKey(x => x.CreatorStyleProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CreatorStyleEvidence>(entity =>
        {
            entity.ToTable("CreatorStyleEvidence");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ExperimentName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.VariantName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Hook).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.SpeakingStyle).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.EditPacing).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.AudioApproach).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.AverageWatchSeconds).HasPrecision(12, 3);
            entity.Property(x => x.VideoDurationSeconds).HasPrecision(12, 3);
            entity.HasIndex(x => new { x.CreatorStyleProfileId, x.StyleExperimentId }).IsUnique();
            entity.HasOne<StyleExperiment>().WithMany().HasForeignKey(x => x.StyleExperimentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<StyleExperimentVariant>().WithMany().HasForeignKey(x => x.WinningVariantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<StyleLearningProposal>(entity =>
        {
            entity.ToTable("StyleLearningProposals");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProposedSummary).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.EvidenceExperimentIdsJson).IsRequired();
            entity.Property(x => x.EvidenceSnapshotJson).IsRequired();
            entity.Property(x => x.AlgorithmVersion).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.ReviewReason).HasMaxLength(2000);
            entity.Property(x => x.ReviewerId).HasMaxLength(160);
            entity.HasOne<CreatorStyleProfile>().WithMany().HasForeignKey(x => x.CreatorStyleProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CreatorStyleProfileId, x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<CreatorStyleProfileVersion>(entity =>
        {
            entity.ToTable("CreatorStyleProfileVersions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Positioning).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.StyleSummary).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(160).IsRequired();
            entity.HasOne<CreatorStyleProfile>().WithMany().HasForeignKey(x => x.CreatorStyleProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<StyleLearningProposal>().WithMany().HasForeignKey(x => x.ApprovedProposalId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.CreatorStyleProfileId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.ApprovedProposalId).IsUnique().HasFilter("[ApprovedProposalId] IS NOT NULL");
        });

        modelBuilder.Entity<StyleExperiment>(entity =>
        {
            entity.ToTable("StyleExperiments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Hypothesis).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Topic).HasMaxLength(200);
            entity.HasIndex(x => new { x.Topic, x.Status, x.UpdatedAt });
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.Status, x.UpdatedAt });
            entity.HasMany(x => x.Variants).WithOne().HasForeignKey(x => x.StyleExperimentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<StyleExperimentVariant>().WithMany().HasForeignKey(x => x.WinningVariantId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ContentProject>().WithMany().HasForeignKey(x => x.ContentProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StyleExperimentVariant>(entity =>
        {
            entity.ToTable("StyleExperimentVariants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Hook).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.SpeakingStyle).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.EditPacing).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.AudioApproach).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Script).HasMaxLength(12000).IsRequired();
            entity.Property(x => x.CreativeNotes).HasMaxLength(4000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(x => x.AverageWatchSeconds).HasPrecision(12, 3);
            entity.Property(x => x.VideoDurationSeconds).HasPrecision(12, 3);
            entity.HasIndex(x => new { x.StyleExperimentId, x.Name }).IsUnique();
        });


        modelBuilder.Entity<StyleVariantMetricObservation>(entity =>
        {
            entity.ToTable("StyleVariantMetricObservations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AverageWatchSeconds).HasPrecision(12, 3);
            entity.Property(x => x.VideoDurationSeconds).HasPrecision(12, 3);
            entity.HasOne<StyleExperimentVariant>().WithMany().HasForeignKey(x => x.StyleExperimentVariantId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.StyleExperimentVariantId, x.ObservedAt }).IsUnique();
            entity.HasIndex(x => new { x.ObservedAt, x.StyleExperimentVariantId });
        });


        modelBuilder.Entity<TrendSelectionFeedback>(entity =>
        {
            entity.ToTable("TrendSelectionFeedback");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.ReasonCode).HasMaxLength(60).IsRequired();
            entity.Property(x => x.ReasonNote).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CandidateSnapshotJson).IsRequired();
            entity.Property(x => x.FeatureSnapshotJson).IsRequired();
            entity.HasOne<DouyinVideoCandidate>().WithMany().HasForeignKey(x => x.DouyinVideoCandidateId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.DouyinVideoCandidateId, x.ReviewedAt });
            entity.HasIndex(x => new { x.Decision, x.ReasonCode, x.ReviewedAt });
        });

        modelBuilder.Entity<TrendCandidateOutcomeObservation>(entity =>
        {
            entity.ToTable("TrendCandidateOutcomeObservations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OutcomeKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.OutcomeValue).HasMaxLength(1000);
            entity.Property(x => x.MetricsSnapshotJson).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(80).IsRequired();
            entity.HasOne<TrendSelectionFeedback>().WithMany().HasForeignKey(x => x.FeedbackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.FeedbackId, x.ObservedAt });
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
            entity.HasOne<ContentTopic>().WithMany().HasForeignKey(x => x.ContentTopicId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
