namespace CreatorFlow.Domain.Discovery;

public enum TrendSignalKind { CreatorChannel, Video, Sound }
public enum TrendPlatform { TikTokVietnam, YouTubeShortsVietnam, FacebookReelsVietnam, Other }

/// <summary>A market observation, not a claim that a platform connector is live.</summary>
public sealed class VietnamTrendSignal
{
    private VietnamTrendSignal() { }
    public Guid Id { get; private set; }
    public TrendSignalKind Kind { get; private set; }
    public TrendPlatform Platform { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string CreatorName { get; private set; } = string.Empty;
    public string SourceUrl { get; private set; } = string.Empty;
    public string? Topic { get; private set; }
    public string? SoundName { get; private set; }
    public string? SoundArtist { get; private set; }
    public long Views { get; private set; }
    public long Likes { get; private set; }
    public long Comments { get; private set; }
    public long Shares { get; private set; }
    public long Saves { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static VietnamTrendSignal Create(TrendSignalKind kind, TrendPlatform platform, string externalId,
        string title, string creatorName, string sourceUrl, string? topic, string? soundName,
        string? soundArtist, long views, long likes, long comments, long shares, long saves,
        DateTimeOffset? publishedAt, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(externalId) || externalId.Trim().Length > 180)
            throw new ArgumentException("External identifier is required and cannot exceed 180 characters.", nameof(externalId));
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A valid HTTPS source URL is required.", nameof(sourceUrl));
        if (title?.Length > 500 || creatorName?.Length > 200 || topic?.Length > 200 ||
            soundName?.Length > 300 || soundArtist?.Length > 200)
            throw new ArgumentException("Trend metadata exceeds its allowed length.");
        if (new[] { views, likes, comments, shares, saves }.Any(x => x < 0))
            throw new ArgumentOutOfRangeException(nameof(views), "Observed metrics cannot be negative.");
        if (kind == TrendSignalKind.Sound && string.IsNullOrWhiteSpace(soundName))
            throw new ArgumentException("Sound observations require a sound name.", nameof(soundName));

        return new VietnamTrendSignal
        {
            Id = Guid.NewGuid(), Kind = kind, Platform = platform, ExternalId = externalId.Trim(),
            Title = title?.Trim() ?? string.Empty, CreatorName = creatorName?.Trim() ?? string.Empty,
            SourceUrl = uri.ToString(), Topic = topic?.Trim(), SoundName = soundName?.Trim(),
            SoundArtist = soundArtist?.Trim(), Views = views, Likes = likes, Comments = comments,
            Shares = shares, Saves = saves, PublishedAt = publishedAt, ObservedAt = observedAt, CreatedAt = observedAt
        };
    }

    public void RecordObservation(long views, long likes, long comments, long shares, long saves, DateTimeOffset observedAt)
    {
        if (new[] { views, likes, comments, shares, saves }.Any(x => x < 0))
            throw new ArgumentOutOfRangeException(nameof(views), "Observed metrics cannot be negative.");
        if (observedAt < ObservedAt) throw new InvalidOperationException("Trend observations must be chronological.");
        Views = views; Likes = likes; Comments = comments; Shares = shares; Saves = saves; ObservedAt = observedAt;
    }
}
