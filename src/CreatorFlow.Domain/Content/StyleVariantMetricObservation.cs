namespace CreatorFlow.Domain.Content;

/// <summary>
/// Immutable point-in-time performance observation. Repeated observations make
/// measurement windows comparable and preserve training provenance.
/// </summary>
public sealed class StyleVariantMetricObservation
{
    private StyleVariantMetricObservation() { }

    public Guid Id { get; private set; }
    public Guid StyleExperimentVariantId { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
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
    public decimal? FollowRate => Views == 0 ? null : (decimal)FollowsGained / Views * 100m;
    public decimal? ProductClickRate => Views == 0 ? null : (decimal)ProductClicks / Views * 100m;
    public decimal? OrderRate => Views == 0 ? null : (decimal)Orders / Views * 100m;
    public decimal? WatchRetentionRate => !AverageWatchSeconds.HasValue || !VideoDurationSeconds.HasValue || VideoDurationSeconds <= 0
        ? null : AverageWatchSeconds / VideoDurationSeconds * 100m;

    public static StyleVariantMetricObservation Capture(StyleExperimentVariant variant, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (variant.Status != StyleVariantStatus.Measured || variant.MeasuredAt != observedAt)
            throw new InvalidOperationException("Observation must capture the exact current measured variant state.");
        return new StyleVariantMetricObservation
        {
            Id = Guid.NewGuid(), StyleExperimentVariantId = variant.Id, ObservedAt = observedAt,
            Views = variant.Views, Likes = variant.Likes, Comments = variant.Comments, Shares = variant.Shares,
            Saves = variant.Saves, FollowsGained = variant.FollowsGained, ProductClicks = variant.ProductClicks,
            Orders = variant.Orders, AverageWatchSeconds = variant.AverageWatchSeconds,
            VideoDurationSeconds = variant.VideoDurationSeconds
        };
    }
}
