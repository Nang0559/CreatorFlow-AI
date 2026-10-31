namespace CreatorFlow.Domain.Content;

public sealed class CreatorStyleProfile
{
    private CreatorStyleProfile() { }
    public Guid Id { get; private set; }
    public string ProfileKey { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Positioning { get; private set; } = string.Empty;
    public string StyleSummary { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<CreatorStyleEvidence> Evidence { get; private set; } = new List<CreatorStyleEvidence>();
    public int EvidenceCount => Evidence.Count;

    public static CreatorStyleProfile Create(string profileKey, string displayName, string? positioning, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(profileKey) || profileKey.Trim().Length > 160)
            throw new ArgumentException("Profile key is required and cannot exceed 160 characters.", nameof(profileKey));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160)
            throw new ArgumentException("Display name is required and cannot exceed 160 characters.", nameof(displayName));
        if (positioning?.Length > 1000) throw new ArgumentException("Positioning cannot exceed 1000 characters.", nameof(positioning));
        return new CreatorStyleProfile
        {
            Id = Guid.NewGuid(), ProfileKey = profileKey.Trim(), DisplayName = displayName.Trim(),
            Positioning = positioning?.Trim() ?? string.Empty, StyleSummary = string.Empty,
            CreatedAt = now, UpdatedAt = now
        };
    }

    public void UpdateGuidance(string displayName, string? positioning, string? styleSummary, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160)
            throw new ArgumentException("Display name is required and cannot exceed 160 characters.", nameof(displayName));
        if (positioning?.Length > 1000 || styleSummary?.Length > 4000)
            throw new ArgumentException("Profile guidance exceeds its allowed length.");
        DisplayName = displayName.Trim();
        Positioning = positioning?.Trim() ?? string.Empty;
        StyleSummary = styleSummary?.Trim() ?? string.Empty;
        UpdatedAt = now;
    }

    public CreatorStyleEvidence LearnFrom(StyleExperiment experiment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (experiment.Status != StyleExperimentStatus.Evaluated || !experiment.WinningVariantId.HasValue)
            throw new InvalidOperationException("Only an evaluated experiment with a confirmed winner can teach the style profile.");
        if (Evidence.Any(x => x.StyleExperimentId == experiment.Id))
            throw new InvalidOperationException("This experiment has already been added to the style profile.");
        var winner = experiment.Variants.Single(x => x.Id == experiment.WinningVariantId.Value);
        if (winner.Status != StyleVariantStatus.Measured)
            throw new InvalidOperationException("Winning variant must have measured results.");
        var evidence = CreatorStyleEvidence.From(Id, experiment.Id, experiment.Name, winner, now);
        Evidence.Add(evidence);
        UpdatedAt = now;
        return evidence;
    }
}

public sealed class CreatorStyleEvidence
{
    private CreatorStyleEvidence() { }
    public Guid Id { get; private set; }
    public Guid CreatorStyleProfileId { get; private set; }
    public Guid StyleExperimentId { get; private set; }
    public Guid WinningVariantId { get; private set; }
    public string ExperimentName { get; private set; } = string.Empty;
    public string VariantName { get; private set; } = string.Empty;
    public string Hook { get; private set; } = string.Empty;
    public string SpeakingStyle { get; private set; } = string.Empty;
    public string EditPacing { get; private set; } = string.Empty;
    public string AudioApproach { get; private set; } = string.Empty;
    public long Views { get; private set; }
    public long Likes { get; private set; }
    public long Comments { get; private set; }
    public long Shares { get; private set; }
    public long Saves { get; private set; }
    public long FollowsGained { get; private set; }
    public long ProductClicks { get; private set; }
    public long Orders { get; private set; }
    public decimal? AverageWatchSeconds { get; private set; }
    public decimal? VideoDurationSeconds { get; private set; }
    public decimal? EngagementRate => Views == 0 ? null : (decimal)(Likes + Comments + Shares + Saves) / Views * 100m;
    public DateTimeOffset LearnedAt { get; private set; }

    internal static CreatorStyleEvidence From(Guid profileId, Guid experimentId, string experimentName, StyleExperimentVariant winner, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), CreatorStyleProfileId = profileId, StyleExperimentId = experimentId, WinningVariantId = winner.Id,
        ExperimentName = experimentName, VariantName = winner.Name, Hook = winner.Hook,
        SpeakingStyle = winner.SpeakingStyle, EditPacing = winner.EditPacing, AudioApproach = winner.AudioApproach,
        Views = winner.Views, Likes = winner.Likes, Comments = winner.Comments, Shares = winner.Shares,
        Saves = winner.Saves, FollowsGained = winner.FollowsGained, ProductClicks = winner.ProductClicks,
        Orders = winner.Orders, AverageWatchSeconds = winner.AverageWatchSeconds,
        VideoDurationSeconds = winner.VideoDurationSeconds, LearnedAt = now
    };
}
