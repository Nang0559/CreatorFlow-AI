namespace CreatorFlow.Domain.Discovery;

public sealed class VietnamTrendMetricSnapshot
{
    private VietnamTrendMetricSnapshot() { }
    public Guid Id { get; private set; }
    public Guid TrendSignalId { get; private set; }
    public long Views { get; private set; }
    public long Likes { get; private set; }
    public long Comments { get; private set; }
    public long Shares { get; private set; }
    public long Saves { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }

    public static VietnamTrendMetricSnapshot Create(Guid trendSignalId, long views, long likes,
        long comments, long shares, long saves, DateTimeOffset observedAt)
    {
        if (trendSignalId == Guid.Empty) throw new ArgumentException("Trend signal ID is required.", nameof(trendSignalId));
        if (new[] { views, likes, comments, shares, saves }.Any(x => x < 0))
            throw new ArgumentOutOfRangeException(nameof(views), "Observed metrics cannot be negative.");
        return new VietnamTrendMetricSnapshot
        {
            Id = Guid.NewGuid(), TrendSignalId = trendSignalId, Views = views, Likes = likes,
            Comments = comments, Shares = shares, Saves = saves, ObservedAt = observedAt
        };
    }
}
