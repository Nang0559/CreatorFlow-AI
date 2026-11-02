using CreatorFlow.Domain.Discovery;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class SharedLinkInboxItemTests
{
    [Theory]
    [InlineData("https://v.douyin.com/AbC123/", "Douyin")]
    [InlineData("https://www.tiktok.com/@creator/video/123", "TikTok")]
    [InlineData("https://youtu.be/abc123", "YouTube")]
    [InlineData("https://www.instagram.com/reel/abc/", "Instagram")]
    public void Create_AcceptsSupportedHttpsShareLinks(string url, string platform)
    {
        var item = SharedLinkInboxItem.Create(url, DateTimeOffset.UtcNow);
        Assert.Equal(platform, item.Platform);
        Assert.Equal(SharedLinkStatus.Pending, item.Status);
        Assert.Equal(64, item.UrlHash.Length);
    }

    [Theory]
    [InlineData("http://www.douyin.com/video/1")]
    [InlineData("https://evil-douyin.com/video/1")]
    [InlineData("https://example.com/video/1")]
    [InlineData("javascript:alert(1)")]
    public void Create_RejectsUnsupportedOrInsecureLinks(string url)
    {
        Assert.Throws<ArgumentException>(() => SharedLinkInboxItem.Create(url, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_DeduplicatesSameCanonicalHostAndPathCaseInsensitively()
    {
        var first = SharedLinkInboxItem.Create("https://V.DOUYIN.COM/AbC?share=1", DateTimeOffset.UtcNow);
        var second = SharedLinkInboxItem.Create("https://v.douyin.com/AbC?share=1", DateTimeOffset.UtcNow);
        Assert.Equal(first.UrlHash, second.UrlHash);
    }

    [Fact]
    public void LinkChannel_ChangesStatusAndStoresChannel()
    {
        var item = SharedLinkInboxItem.Create("https://v.douyin.com/AbC", DateTimeOffset.UtcNow);
        var channelId = Guid.NewGuid();
        item.LinkChannel(channelId);
        Assert.Equal(SharedLinkStatus.Linked, item.Status);
        Assert.Equal(channelId, item.LinkedChannelId);
    }

    [Fact]
    public void IgnoredLinkCannotBeLinked()
    {
        var item = SharedLinkInboxItem.Create("https://v.douyin.com/AbC", DateTimeOffset.UtcNow);
        item.Ignore("Not relevant");
        Assert.Throws<InvalidOperationException>(() => item.LinkChannel(Guid.NewGuid()));
    }
}
