using CreatorFlow.Domain.Discovery;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class DouyinDiscoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Topic_normalizes_keywords_and_can_be_deactivated()
    {
        var topic = ContentTopic.Create("  Home gadgets ", "kitchen, home\nkitchen", Now);
        Assert.Equal("Home gadgets", topic.Name);
        Assert.Equal("kitchen\nhome", topic.Keywords);
        topic.Deactivate(Now.AddMinutes(1));
        Assert.False(topic.IsActive);
        Assert.Throws<InvalidOperationException>(() => topic.Update("Other", null, Now.AddMinutes(2)));
    }

    [Fact]
    public void Topic_rejects_blank_name()
    {
        Assert.Throws<ArgumentException>(() => ContentTopic.Create(" ", null, Now));
    }

    [Fact]
    public void Source_channel_can_be_followed_and_unfollowed()
    {
        var channel = DouyinSourceChannel.Create("sec-user-1", "Creator", "https://www.douyin.com/user/sec-user-1", Now);
        channel.Follow(Now.AddMinutes(1));
        Assert.True(channel.IsFollowed);
        Assert.Equal(Now.AddMinutes(1), channel.FollowedAt);
        channel.Unfollow(Now.AddMinutes(2));
        Assert.False(channel.IsFollowed);
        Assert.Null(channel.FollowedAt);
    }

    [Fact]
    public void Source_channel_rejects_non_https_profile()
    {
        Assert.Throws<ArgumentException>(() => DouyinSourceChannel.Create("id", "Creator", "http://example.com", Now));
    }

    [Fact]
    public void Channel_check_timestamp_cannot_move_backwards()
    {
        var channel = DouyinSourceChannel.Create("id", "Creator", "https://www.douyin.com/user/id", Now);
        channel.MarkChecked(Now.AddHours(1));
        Assert.Throws<InvalidOperationException>(() => channel.MarkChecked(Now));
    }

    [Fact]
    public void Followed_channel_video_requires_channel_reference()
    {
        Assert.Throws<ArgumentException>(() => DouyinVideoCandidate.Create(
            "video-1", null, "https://www.douyin.com/video/1", "Title",
            VideoDiscoveryMethod.FollowedChannel, null, Now));
    }

    [Fact]
    public void Explored_video_can_be_selected_then_queued_for_adaptation()
    {
        var video = DouyinVideoCandidate.Create("video-1", null, "https://www.douyin.com/video/1",
            "Demo", VideoDiscoveryMethod.TopicExplore, Now.AddDays(-1), Now);
        Assert.Equal(VideoCandidateStatus.Candidate, video.Status);
        video.Select(Now.AddMinutes(1));
        video.QueueForAdaptation(Now.AddMinutes(2));
        Assert.Equal(VideoCandidateStatus.QueuedForAdaptation, video.Status);
        video.MarkAdapted(Now.AddMinutes(3));
        Assert.Equal(VideoCandidateStatus.Adapted, video.Status);
    }

    [Fact]
    public void Candidate_must_be_selected_before_adaptation()
    {
        var video = DouyinVideoCandidate.Create("video-1", null, "https://www.douyin.com/video/1",
            null, VideoDiscoveryMethod.TopicExplore, null, Now);
        Assert.Throws<InvalidOperationException>(() => video.QueueForAdaptation(Now));
    }

    [Fact]
    public void Rejected_video_cannot_be_selected_again()
    {
        var video = DouyinVideoCandidate.Create("video-1", null, "https://www.douyin.com/video/1",
            null, VideoDiscoveryMethod.Manual, null, Now);
        video.Reject(Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => video.Select(Now.AddMinutes(2)));
    }

    [Fact]
    public void Video_source_url_must_be_https()
    {
        Assert.Throws<ArgumentException>(() => DouyinVideoCandidate.Create(
            "video-1", null, "http://example.com/video", null, VideoDiscoveryMethod.Manual, null, Now));
    }
}
