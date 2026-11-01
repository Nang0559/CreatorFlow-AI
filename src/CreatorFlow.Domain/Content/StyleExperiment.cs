namespace CreatorFlow.Domain.Content;

public enum StyleExperimentStatus { Draft, Running, Evaluated, Archived }
public enum StyleVariantStatus { Draft, Ready, Published, Measured }

public sealed class StyleExperiment
{
    private StyleExperiment() { }
    public Guid Id { get; private set; }
    public Guid? ContentProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Hypothesis { get; private set; } = string.Empty;
    public string? Topic { get; private set; }
    public StyleExperimentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ICollection<StyleExperimentVariant> Variants { get; private set; } = new List<StyleExperimentVariant>();
    public Guid? WinningVariantId { get; private set; }

    public static StyleExperiment Create(string name, string hypothesis, IEnumerable<StyleVariantDraft> variants,
        DateTimeOffset now, Guid? contentProjectId = null, string? topic = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 160)
            throw new ArgumentException("Experiment name is required and cannot exceed 160 characters.", nameof(name));
        if (string.IsNullOrWhiteSpace(hypothesis) || hypothesis.Trim().Length > 2000)
            throw new ArgumentException("A testable hypothesis is required (max 2000 characters).", nameof(hypothesis));
        if (topic?.Trim().Length > 200)
            throw new ArgumentException("Topic cannot exceed 200 characters.", nameof(topic));
        var drafts = variants?.ToArray() ?? [];
        if (drafts.Length is < 2 or > 3)
            throw new ArgumentException("Each experiment must contain exactly 2 or 3 style variants.", nameof(variants));
        if (drafts.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Hook) ||
                            string.IsNullOrWhiteSpace(x.SpeakingStyle) || string.IsNullOrWhiteSpace(x.EditPacing) ||
                            string.IsNullOrWhiteSpace(x.AudioApproach) ||
                            string.IsNullOrWhiteSpace(x.Script)))
            throw new ArgumentException("Every variant needs a name, hook, speaking style, edit pacing and script.", nameof(variants));
        if (drafts.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != drafts.Length)
            throw new ArgumentException("Variant names must be unique within an experiment.", nameof(variants));

        var experiment = new StyleExperiment
        {
            Id = Guid.NewGuid(), ContentProjectId = contentProjectId, Name = name.Trim(),
            Hypothesis = hypothesis.Trim(), Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim(),
            Status = StyleExperimentStatus.Draft, CreatedAt = now, UpdatedAt = now
        };
        foreach (var draft in drafts)
            experiment.Variants.Add(StyleExperimentVariant.Create(experiment.Id, draft));
        return experiment;
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != StyleExperimentStatus.Draft) throw new InvalidOperationException("Only draft experiments can start.");
        if (Variants.Count is < 2 or > 3 || Variants.Any(x => x.Status != StyleVariantStatus.Ready))
            throw new InvalidOperationException("All 2 or 3 variants must be ready before starting.");
        Status = StyleExperimentStatus.Running; UpdatedAt = now;
    }

    public void Evaluate(Guid winningVariantId, DateTimeOffset now)
    {
        if (Status != StyleExperimentStatus.Running) throw new InvalidOperationException("Only running experiments can be evaluated.");
        var winner = Variants.SingleOrDefault(x => x.Id == winningVariantId);
        if (winner is null || winner.Status != StyleVariantStatus.Measured)
            throw new InvalidOperationException("The winning variant must belong to this experiment and have measured results.");
        if (Variants.Any(x => x.Status != StyleVariantStatus.Measured))
            throw new InvalidOperationException("All variants must have measurement snapshots before evaluation.");
        WinningVariantId = winner.Id; Status = StyleExperimentStatus.Evaluated; UpdatedAt = now;
    }
}

public sealed record StyleVariantDraft(string Name, string Hook, string SpeakingStyle, string EditPacing,
    string AudioApproach, string Script, string? CreativeNotes = null);

public sealed class StyleExperimentVariant
{
    private StyleExperimentVariant() { }
    public Guid Id { get; private set; }
    public Guid StyleExperimentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Hook { get; private set; } = string.Empty;
    public string SpeakingStyle { get; private set; } = string.Empty;
    public string EditPacing { get; private set; } = string.Empty;
    public string AudioApproach { get; private set; } = string.Empty;
    public string Script { get; private set; } = string.Empty;
    public string? CreativeNotes { get; private set; }
    public StyleVariantStatus Status { get; private set; }
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
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? MeasuredAt { get; private set; }
    public decimal? EngagementRate => Views == 0 ? null : (decimal)(Likes + Comments + Shares + Saves) / Views * 100m;

    internal static StyleExperimentVariant Create(Guid experimentId, StyleVariantDraft draft) => new()
    {
        Id = Guid.NewGuid(), StyleExperimentId = experimentId, Name = draft.Name.Trim(),
        Hook = draft.Hook.Trim(), SpeakingStyle = draft.SpeakingStyle.Trim(), EditPacing = draft.EditPacing.Trim(),
        AudioApproach = draft.AudioApproach?.Trim() ?? string.Empty, Script = draft.Script.Trim(),
        CreativeNotes = draft.CreativeNotes?.Trim(), Status = StyleVariantStatus.Draft
    };

    public void MarkReady()
    {
        if (Status != StyleVariantStatus.Draft) throw new InvalidOperationException("Only draft variants can be marked ready.");
        Status = StyleVariantStatus.Ready;
    }

    public void MarkPublished(DateTimeOffset publishedAt)
    {
        if (Status != StyleVariantStatus.Ready) throw new InvalidOperationException("Only ready variants can be published.");
        PublishedAt = publishedAt; Status = StyleVariantStatus.Published;
    }

    public void RecordMetrics(long views, long likes, long comments, long shares, long saves,
        long followsGained, long productClicks, long orders, decimal? averageWatchSeconds,
        decimal? videoDurationSeconds, DateTimeOffset measuredAt)
    {
        if (Status is not (StyleVariantStatus.Published or StyleVariantStatus.Measured))
            throw new InvalidOperationException("Only published variants can be measured.");
        if (Status == StyleVariantStatus.Measured && MeasuredAt.HasValue && measuredAt <= MeasuredAt.Value)
            throw new ArgumentOutOfRangeException(nameof(measuredAt), "A new measurement must be later than the previous observation.");
        if (new[] { views, likes, comments, shares, saves, followsGained, productClicks, orders }.Any(x => x < 0))
            throw new ArgumentOutOfRangeException(nameof(views), "Metrics cannot be negative.");
        if (PublishedAt.HasValue && measuredAt < PublishedAt.Value)
            throw new ArgumentOutOfRangeException(nameof(measuredAt), "Measurement cannot precede publication.");
        if (averageWatchSeconds < 0 || videoDurationSeconds < 0 ||
            (averageWatchSeconds.HasValue && videoDurationSeconds.HasValue && averageWatchSeconds > videoDurationSeconds))
            throw new ArgumentOutOfRangeException(nameof(averageWatchSeconds), "Watch duration metrics are invalid.");
        Views = views; Likes = likes; Comments = comments; Shares = shares; Saves = saves;
        FollowsGained = followsGained; ProductClicks = productClicks; Orders = orders;
        AverageWatchSeconds = averageWatchSeconds; VideoDurationSeconds = videoDurationSeconds;
        MeasuredAt = measuredAt; Status = StyleVariantStatus.Measured;
    }
}
