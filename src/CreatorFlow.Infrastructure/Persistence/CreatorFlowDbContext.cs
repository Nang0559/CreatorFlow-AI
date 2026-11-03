using CreatorFlow.Domain.Content;
using CreatorFlow.Domain.Discovery;
using CreatorFlow.Domain.Identity;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CreatorFlow.Infrastructure.Persistence;

public sealed class CreatorFlowDbContext(DbContextOptions<CreatorFlowDbContext> options, ITenantContext? tenantContext = null) : DbContext(options)
{
    private static readonly Guid InMemoryTestTenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private Guid CurrentTenantId => tenantContext?.TenantId ??
        (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory" ? InMemoryTestTenantId : Guid.Empty);
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<ContentProject> ContentProjects => Set<ContentProject>();
    public DbSet<ContentTopic> ContentTopics => Set<ContentTopic>();
    public DbSet<DouyinSourceChannel> DouyinSourceChannels => Set<DouyinSourceChannel>();
    public DbSet<SharedLinkInboxItem> SharedLinkInboxItems => Set<SharedLinkInboxItem>();
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
    public DbSet<AIProviderConfiguration> AIProviderConfigurations => Set<AIProviderConfiguration>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenantOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampTenantOwnership()
    {
        foreach (var entry in ChangeTracker.Entries().Where(x =>
                     x.State is EntityState.Added or EntityState.Modified &&
                     x.Entity is not Tenant && x.Entity is not TenantMembership))
        {
            var tenantId = tenantContext?.TenantId;
            if ((!tenantId.HasValue || tenantId.Value == Guid.Empty) &&
                Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
                tenantId = InMemoryTestTenantId;
            if (!tenantId.HasValue || tenantId.Value == Guid.Empty)
                throw new InvalidOperationException("A verified tenant context is required to write tenant-owned data.");

            var tenantProperty = entry.Property("TenantId");
            if (entry.State == EntityState.Added)
                tenantProperty.CurrentValue = tenantId.Value;
            else if (!Equals(tenantProperty.OriginalValue, tenantId.Value) ||
                     !Equals(tenantProperty.CurrentValue, tenantId.Value))
                throw new InvalidOperationException("Tenant ownership is immutable; cross-tenant row moves are forbidden.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Establish the tenant key type for every tenant-owned entity before any relationship
        // can cause EF conventions to create an incorrectly typed shadow FK property.
        var tenantOwnedTypes = new[]
        {
            typeof(TenantInvitation),
            typeof(ContentProject), typeof(ContentTopic), typeof(DouyinSourceChannel),
            typeof(SharedLinkInboxItem), typeof(DouyinVideoCandidate),
            typeof(TrendSelectionFeedback), typeof(TrendCandidateOutcomeObservation),
            typeof(VietnamTrendSignal), typeof(VietnamTrendMetricSnapshot),
            typeof(StyleExperiment), typeof(StyleExperimentVariant),
            typeof(CreatorStyleProfile), typeof(CreatorStyleEvidence),
            typeof(StyleVariantMetricObservation), typeof(StyleLearningProposal),
            typeof(CreatorStyleProfileVersion), typeof(AIProviderConfiguration)
        };
        foreach (var tenantOwnedType in tenantOwnedTypes)
            modelBuilder.Entity(tenantOwnedType).Property<Guid>("TenantId").IsRequired();
        modelBuilder.Entity<AIProviderConfiguration>(entity =>
        {
            entity.ToTable("AIProviders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ProviderType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Model).HasMaxLength(160).IsRequired();
            entity.Property(x => x.EncryptedApiKey).IsRequired(false);
            entity.Property(x => x.RoutingRole).HasMaxLength(16).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.TenantId, x.IsActive, x.RoutingRole, x.FallbackOrder });
        });

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
            entity.Property(x => x.VerifiedEmail).HasMaxLength(320);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.TenantId, x.IdentityIssuer, x.SubjectId }).IsUnique();
            entity.HasIndex(x => new { x.IdentityIssuer, x.SubjectId, x.Status });
        });

        modelBuilder.Entity<TenantInvitation>(entity =>
        {
            entity.ToTable("TenantInvitations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.CreatedByIssuer).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatedBySubject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AcceptedByIssuer).HasMaxLength(500);
            entity.Property(x => x.AcceptedBySubject).HasMaxLength(200);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.Status, x.ExpiresAt });
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
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasIndex("TenantId", nameof(DouyinSourceChannel.DouyinSecUserId)).IsUnique();
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
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasIndex("TenantId", nameof(VietnamTrendSignal.Platform), nameof(VietnamTrendSignal.Kind), nameof(VietnamTrendSignal.ExternalId)).IsUnique();
            entity.HasIndex(x => new { x.Kind, x.ObservedAt });
        });

        modelBuilder.Entity<VietnamTrendMetricSnapshot>(entity =>
        {
            entity.ToTable("VietnamTrendMetricSnapshots");
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.HasOne<VietnamTrendSignal>().WithMany()
                .HasForeignKey("TenantId", nameof(VietnamTrendMetricSnapshot.TrendSignalId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_VietnamTrendMetricSnapshots_Tenant_Signal");
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
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasIndex("TenantId", nameof(CreatorStyleProfile.ProfileKey)).IsUnique();
            entity.HasMany(x => x.Evidence).WithOne()
                .HasForeignKey("TenantId", "CreatorStyleProfileId").HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_CreatorStyleEvidence_Tenant_Profile");
        });

        modelBuilder.Entity<CreatorStyleEvidence>(entity =>
        {
            entity.ToTable("CreatorStyleEvidence");
            entity.Property<Guid>("TenantId").IsRequired();
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
            entity.HasOne<StyleExperiment>().WithMany()
                .HasForeignKey("TenantId", "StyleExperimentId").HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_CreatorStyleEvidence_Tenant_Experiment");
            entity.HasOne<StyleExperimentVariant>().WithMany()
                .HasForeignKey("TenantId", "WinningVariantId").HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_CreatorStyleEvidence_Tenant_WinningVariant");
        });

        modelBuilder.Entity<StyleLearningProposal>(entity =>
        {
            entity.ToTable("StyleLearningProposals");
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProposedSummary).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.EvidenceExperimentIdsJson).IsRequired();
            entity.Property(x => x.EvidenceSnapshotJson).IsRequired();
            entity.Property(x => x.AlgorithmVersion).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.ReviewReason).HasMaxLength(2000);
            entity.Property(x => x.ReviewerId).HasMaxLength(160);
            entity.HasOne<CreatorStyleProfile>().WithMany()
                .HasForeignKey("TenantId", nameof(StyleLearningProposal.CreatorStyleProfileId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_StyleLearningProposals_Tenant_Profile");
            entity.HasIndex(x => new { x.CreatorStyleProfileId, x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<CreatorStyleProfileVersion>(entity =>
        {
            entity.ToTable("CreatorStyleProfileVersions");
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Positioning).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.StyleSummary).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.CreatedBy).HasMaxLength(160).IsRequired();
            entity.HasOne<CreatorStyleProfile>().WithMany()
                .HasForeignKey("TenantId", nameof(CreatorStyleProfileVersion.CreatorStyleProfileId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_CreatorStyleProfileVersions_Tenant_Profile");
            entity.HasOne<StyleLearningProposal>().WithMany()
                .HasForeignKey("TenantId", nameof(CreatorStyleProfileVersion.ApprovedProposalId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_CreatorStyleProfileVersions_Tenant_Proposal");
            entity.HasIndex(x => new { x.CreatorStyleProfileId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.ApprovedProposalId).IsUnique().HasFilter("[ApprovedProposalId] IS NOT NULL");
        });

        modelBuilder.Entity<StyleExperiment>(entity =>
        {
            entity.ToTable("StyleExperiments");
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Hypothesis).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Topic).HasMaxLength(200);
            entity.HasIndex(x => new { x.Topic, x.Status, x.UpdatedAt });
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(x => new { x.Status, x.UpdatedAt });
            entity.HasMany(x => x.Variants).WithOne()
                .HasForeignKey("TenantId", nameof(StyleExperimentVariant.StyleExperimentId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_StyleExperimentVariants_Tenant_Experiment");
            entity.HasOne<StyleExperimentVariant>().WithMany()
                .HasForeignKey("TenantId", nameof(StyleExperiment.WinningVariantId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_StyleExperiments_Tenant_WinningVariant");
            entity.HasOne<ContentProject>().WithMany()
                .HasForeignKey("TenantId", nameof(StyleExperiment.ContentProjectId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_StyleExperiments_Tenant_ContentProject");
        });

        modelBuilder.Entity<StyleExperimentVariant>(entity =>
        {
            entity.ToTable("StyleExperimentVariants");
            entity.Property<Guid>("TenantId").IsRequired();
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
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AverageWatchSeconds).HasPrecision(12, 3);
            entity.Property(x => x.VideoDurationSeconds).HasPrecision(12, 3);
            entity.HasOne<StyleExperimentVariant>().WithMany()
                .HasForeignKey("TenantId", nameof(StyleVariantMetricObservation.StyleExperimentVariantId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_StyleVariantMetricObservations_Tenant_Variant");
            entity.HasIndex(x => new { x.StyleExperimentVariantId, x.ObservedAt }).IsUnique();
            entity.HasIndex(x => new { x.ObservedAt, x.StyleExperimentVariantId });
        });


        modelBuilder.Entity<TrendSelectionFeedback>(entity =>
        {
            entity.ToTable("TrendSelectionFeedback");
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Decision).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.ReasonCode).HasMaxLength(60).IsRequired();
            entity.Property(x => x.ReasonNote).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CandidateSnapshotJson).IsRequired();
            entity.Property(x => x.FeatureSnapshotJson).IsRequired();
            entity.HasOne<DouyinVideoCandidate>().WithMany()
                .HasForeignKey("TenantId", "DouyinVideoCandidateId").HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_TrendSelectionFeedback_Tenant_Candidate");
            entity.HasIndex(x => new { x.DouyinVideoCandidateId, x.ReviewedAt });
            entity.HasIndex(x => new { x.Decision, x.ReasonCode, x.ReviewedAt });
        });

        modelBuilder.Entity<TrendCandidateOutcomeObservation>(entity =>
        {
            entity.ToTable("TrendCandidateOutcomeObservations");
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OutcomeKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(x => x.OutcomeValue).HasMaxLength(1000);
            entity.Property(x => x.MetricsSnapshotJson).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(80).IsRequired();
            entity.HasOne<TrendSelectionFeedback>().WithMany()
                .HasForeignKey("TenantId", nameof(TrendCandidateOutcomeObservation.FeedbackId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_TrendCandidateOutcomeObservations_Tenant_Feedback");
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
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasIndex("TenantId", nameof(DouyinVideoCandidate.DouyinVideoId)).IsUnique();
            entity.HasIndex(x => new { x.Status, x.DiscoveredAt });
            entity.HasOne<DouyinSourceChannel>().WithMany()
                .HasForeignKey("TenantId", nameof(DouyinVideoCandidate.SourceChannelId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_DouyinVideoCandidates_Tenant_Channel");
            entity.HasOne<ContentTopic>().WithMany()
                .HasForeignKey("TenantId", nameof(DouyinVideoCandidate.ContentTopicId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_DouyinVideoCandidates_Tenant_Topic");
        });
        modelBuilder.Entity<SharedLinkInboxItem>(entity =>
        {
            entity.ToTable("SharedLinkInboxItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Platform).HasMaxLength(24).IsRequired();
            entity.Property(x => x.SharedUrl).HasMaxLength(1200).IsRequired();
            entity.Property(x => x.UrlHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(x => x.ReviewNote).HasMaxLength(500);
            entity.Property<Guid>("TenantId").IsRequired();
            entity.HasOne<DouyinSourceChannel>().WithMany()
                .HasForeignKey("TenantId", nameof(SharedLinkInboxItem.LinkedChannelId)).HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SharedLinkInboxItems_Tenant_Channel");
            entity.HasIndex("TenantId", nameof(SharedLinkInboxItem.UrlHash)).IsUnique();
            entity.HasIndex("TenantId", nameof(SharedLinkInboxItem.Status), nameof(SharedLinkInboxItem.SharedAt));
        });

        // Apply one tenant filter and tenant FK to every business entity. Identity tables are
        // intentionally unfiltered because membership resolution happens before authorization.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(x => x.ClrType != typeof(Tenant) && x.ClrType != typeof(TenantMembership))
                     .ToArray())
        {
            var entityBuilder = modelBuilder.Entity(entityType.ClrType);
            entityBuilder.Property<Guid>("TenantId").IsRequired();
            entityBuilder.HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId")
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName($"FK_{entityType.GetTableName()}_Tenants");
            entityBuilder.HasIndex("TenantId");

            var parameter = Expression.Parameter(entityType.ClrType, "row");
            var tenantProperty = Expression.Call(typeof(EF), nameof(EF.Property),
                new[] { typeof(Guid) }, parameter, Expression.Constant("TenantId"));
            var currentTenant = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
            var filter = Expression.Lambda(Expression.Equal(tenantProperty, currentTenant), parameter);
            entityBuilder.HasQueryFilter(filter);
        }
    }
}
