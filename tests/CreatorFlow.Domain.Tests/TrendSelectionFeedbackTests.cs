using CreatorFlow.Domain.Discovery;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class TrendSelectionFeedbackTests
{
    private static readonly DateTimeOffset ReviewedAt =
        new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TrendSelectionDecision.Select)]
    [InlineData(TrendSelectionDecision.Reject)]
    [InlineData(TrendSelectionDecision.Watch)]
    public void Create_preserves_creator_decision_and_allows_empty_note(TrendSelectionDecision decision)
    {
        var candidateId = Guid.NewGuid();

        var feedback = TrendSelectionFeedback.Create(candidateId, decision, "Hook", "",
            "{\"videoId\":\"source-1\"}", "{\"schemaVersion\":1}", ReviewedAt);

        Assert.NotEqual(Guid.Empty, feedback.Id);
        Assert.Equal(candidateId, feedback.DouyinVideoCandidateId);
        Assert.Equal(decision, feedback.Decision);
        Assert.Equal("Hook", feedback.ReasonCode);
        Assert.Equal(string.Empty, feedback.ReasonNote);
        Assert.Equal("{\"schemaVersion\":1}", feedback.FeatureSnapshotJson);
        Assert.Equal(ReviewedAt, feedback.ReviewedAt);
    }

    [Fact]
    public void Create_rejects_undefined_numeric_decision()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), (TrendSelectionDecision)99, "Hook", null, "{}", "{}", ReviewedAt));
    }

    [Fact]
    public void Create_rejects_missing_candidate_and_reason()
    {
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.Empty, TrendSelectionDecision.Select, "Hook", null, "{}", "{}", ReviewedAt));
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, " ", null, "{}", "{}", ReviewedAt));
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, new string('x', 61), null, "{}", "{}", ReviewedAt));
    }

    [Fact]
    public void Create_rejects_invalid_or_non_object_json_snapshots()
    {
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, "Hook", null, "{invalid", "{}", ReviewedAt));
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, "Hook", null, "[]", "{}", ReviewedAt));
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, "Hook", null, "{}", "null", ReviewedAt));
    }

    [Fact]
    public void Create_rejects_oversized_note_and_snapshots()
    {
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, "Hook", new string('n', 2001), "{}", "{}", ReviewedAt));
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, "Hook", null, new string('x', 50001), "{}", ReviewedAt));
        Assert.Throws<ArgumentException>(() => TrendSelectionFeedback.Create(
            Guid.NewGuid(), TrendSelectionDecision.Select, "Hook", null, "{}", new string('x', 50001), ReviewedAt));
    }
}
