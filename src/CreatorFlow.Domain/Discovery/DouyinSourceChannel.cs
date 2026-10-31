namespace CreatorFlow.Domain.Discovery;

public sealed class DouyinSourceChannel
{
    private DouyinSourceChannel() { }

    public Guid Id { get; private set; }
    public string DouyinSecUserId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string ProfileUrl { get; private set; } = string.Empty;
    public bool IsFollowed { get; private set; }
    public DateTimeOffset? FollowedAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static DouyinSourceChannel Create(string secUserId, string displayName, string profileUrl, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secUserId) || secUserId.Trim().Length > 160)
            throw new ArgumentException("A valid Douyin channel identifier is required.", nameof(secUserId));
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 160)
            throw new ArgumentException("A channel display name is required and cannot exceed 160 characters.", nameof(displayName));
        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("A valid HTTPS Douyin profile URL is required.", nameof(profileUrl));

        return new DouyinSourceChannel
        {
            Id = Guid.NewGuid(), DouyinSecUserId = secUserId.Trim(),
            DisplayName = displayName.Trim(), ProfileUrl = uri.ToString(),
            CreatedAt = now, UpdatedAt = now
        };
    }

    public void Follow(DateTimeOffset now)
    {
        if (IsFollowed) return;
        IsFollowed = true;
        FollowedAt = now;
        UpdatedAt = now;
    }

    public void Unfollow(DateTimeOffset now)
    {
        if (!IsFollowed) return;
        IsFollowed = false;
        FollowedAt = null;
        UpdatedAt = now;
    }

    public void MarkChecked(DateTimeOffset checkedAt)
    {
        if (LastCheckedAt.HasValue && checkedAt < LastCheckedAt.Value)
            throw new InvalidOperationException("Channel check time cannot move backwards.");
        LastCheckedAt = checkedAt;
        UpdatedAt = checkedAt;
    }
}
