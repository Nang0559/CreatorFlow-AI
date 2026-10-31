namespace CreatorFlow.Domain.Discovery;

public enum VideoDiscoveryMethod { FollowedChannel, TopicExplore, Manual }
public enum VideoCandidateStatus { Candidate, Selected, Rejected, QueuedForAdaptation, Adapted }

public sealed class DouyinVideoCandidate
{
    private DouyinVideoCandidate() { }

    public Guid Id { get; private set; }
    public string DouyinVideoId { get; private set; } = string.Empty;
    public Guid? SourceChannelId { get; private set; }
    public string SourceUrl { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public VideoDiscoveryMethod DiscoveryMethod { get; private set; }
    public VideoCandidateStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset DiscoveredAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static DouyinVideoCandidate Create(
        string videoId, Guid? sourceChannelId, string sourceUrl, string? title,
        VideoDiscoveryMethod discoveryMethod, DateTimeOffset? publishedAt, DateTimeOffset discoveredAt)
    {
        if (string.IsNullOrWhiteSpace(videoId) || videoId.Trim().Length > 160)
            throw new ArgumentException("A valid Douyin video identifier is required.", nameof(videoId));
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A valid HTTPS source video URL is required.", nameof(sourceUrl));
        if (discoveryMethod == VideoDiscoveryMethod.FollowedChannel && sourceChannelId is null)
            throw new ArgumentException("Followed-channel discoveries must reference a source channel.", nameof(sourceChannelId));
        if (title?.Length > 500)
            throw new ArgumentException("Video title cannot exceed 500 characters.", nameof(title));

        return new DouyinVideoCandidate
        {
            Id = Guid.NewGuid(), DouyinVideoId = videoId.Trim(), SourceChannelId = sourceChannelId,
            SourceUrl = uri.ToString(), Title = title?.Trim() ?? string.Empty,
            DiscoveryMethod = discoveryMethod, Status = VideoCandidateStatus.Candidate,
            PublishedAt = publishedAt, DiscoveredAt = discoveredAt, UpdatedAt = discoveredAt
        };
    }

    public void Select(DateTimeOffset now)
    {
        if (Status is VideoCandidateStatus.Rejected or VideoCandidateStatus.Adapted)
            throw new InvalidOperationException("Rejected or already adapted videos cannot be selected.");
        Status = VideoCandidateStatus.Selected;
        UpdatedAt = now;
    }

    public void Reject(DateTimeOffset now)
    {
        if (Status is VideoCandidateStatus.QueuedForAdaptation or VideoCandidateStatus.Adapted)
            throw new InvalidOperationException("A video already in adaptation cannot be rejected.");
        Status = VideoCandidateStatus.Rejected;
        UpdatedAt = now;
    }

    public void QueueForAdaptation(DateTimeOffset now)
    {
        if (Status != VideoCandidateStatus.Selected)
            throw new InvalidOperationException("Only selected videos can enter the adaptation queue.");
        Status = VideoCandidateStatus.QueuedForAdaptation;
        UpdatedAt = now;
    }

    public void MarkAdapted(DateTimeOffset now)
    {
        if (Status != VideoCandidateStatus.QueuedForAdaptation)
            throw new InvalidOperationException("Only queued videos can be marked as adapted.");
        Status = VideoCandidateStatus.Adapted;
        UpdatedAt = now;
    }
}
