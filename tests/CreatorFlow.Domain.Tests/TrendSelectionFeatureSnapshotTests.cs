using System.Text.Json;
using CreatorFlow.Domain.Discovery;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class TrendSelectionFeatureSnapshotTests
{
    [Fact]
    public void Create_extracts_versioned_metadata_without_copying_decision_or_mutable_status()
    {
        var discoveredAt = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var publishedAt = discoveredAt.AddHours(-12);
        var extractedAt = discoveredAt.AddHours(2);
        var candidate = DouyinVideoCandidate.Create(
            "douyin-001", Guid.NewGuid(), "https://www.douyin.com/video/001",
            "3 mẹo hay #review cho bạn?", VideoDiscoveryMethod.FollowedChannel,
            publishedAt, discoveredAt, Guid.NewGuid());

        var snapshot = TrendSelectionFeatureSnapshot.Create(candidate, extractedAt);
        using var json = JsonDocument.Parse(snapshot.ToJson());
        var root = json.RootElement;

        Assert.Equal(TrendSelectionFeatureSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal("creatorflow.trend-selection", root.GetProperty("schema").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("FollowedChannel", root.GetProperty("discoveryMethod").GetString());
        Assert.True(root.GetProperty("hasSourceChannel").GetBoolean());
        Assert.True(root.GetProperty("hasTopic").GetBoolean());
        Assert.Equal(25, root.GetProperty("titleCharacterCount").GetInt32());
        Assert.Equal(5, root.GetProperty("titleTokenCount").GetInt32());
        Assert.True(root.GetProperty("titleHasQuestionMark").GetBoolean());
        Assert.True(root.GetProperty("titleHasDigits").GetBoolean());
        Assert.Equal(1, root.GetProperty("titleHashtagCount").GetInt32());
        Assert.Equal(14d, root.GetProperty("publishedAgeHours").GetDouble());
        Assert.Equal(2d, root.GetProperty("discoveredAgeHours").GetDouble());
        Assert.False(root.TryGetProperty("decision", out _));
        Assert.False(root.TryGetProperty("status", out _));
    }

    [Fact]
    public void Create_keeps_missing_publication_age_as_null()
    {
        var discoveredAt = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var candidate = DouyinVideoCandidate.Create(
            "manual-001", null, "https://www.douyin.com/video/manual-001",
            null, VideoDiscoveryMethod.Manual, null, discoveredAt);

        var snapshot = TrendSelectionFeatureSnapshot.Create(candidate, discoveredAt.AddMinutes(30));

        Assert.Null(snapshot.PublishedAgeHours);
        Assert.Equal(0.5d, snapshot.DiscoveredAgeHours);
        Assert.Equal(0, snapshot.TitleTokenCount);
        Assert.False(snapshot.HasSourceChannel);
        Assert.False(snapshot.HasTopic);
    }

    [Fact]
    public void Create_rejects_null_candidate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TrendSelectionFeatureSnapshot.Create(null!, DateTimeOffset.UtcNow));
    }
}
