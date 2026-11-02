using CreatorFlow.Domain.Discovery;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class TrendCandidateOutcomeObservationTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 11, 1, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TrendCandidateOutcomeKind.Adapted)]
    [InlineData(TrendCandidateOutcomeKind.Published)]
    [InlineData(TrendCandidateOutcomeKind.MarketPerformance)]
    [InlineData(TrendCandidateOutcomeKind.CreatorAssessment)]
    public void Create_preserves_outcome_and_source(TrendCandidateOutcomeKind kind)
    {
        var feedbackId = Guid.NewGuid();

        var outcome = TrendCandidateOutcomeObservation.Create(feedbackId, kind, "Published successfully",
            "{\"views\":1200}", "OfficialAnalyticsApi", ObservedAt);

        Assert.NotEqual(Guid.Empty, outcome.Id);
        Assert.Equal(feedbackId, outcome.FeedbackId);
        Assert.Equal(kind, outcome.OutcomeKind);
        Assert.Equal("Published successfully", outcome.OutcomeValue);
        Assert.Equal("{\"views\":1200}", outcome.MetricsSnapshotJson);
        Assert.Equal("OfficialAnalyticsApi", outcome.Source);
        Assert.Equal(ObservedAt, outcome.ObservedAt);
    }

    [Fact]
    public void Create_rejects_invalid_kind_feedback_source_and_metrics()
    {
        Assert.Throws<ArgumentException>(() => TrendCandidateOutcomeObservation.Create(
            Guid.Empty, TrendCandidateOutcomeKind.Published, null, "{}", "Manual", ObservedAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrendCandidateOutcomeObservation.Create(
            Guid.NewGuid(), (TrendCandidateOutcomeKind)99, null, "{}", "Manual", ObservedAt));
        Assert.Throws<ArgumentException>(() => TrendCandidateOutcomeObservation.Create(
            Guid.NewGuid(), TrendCandidateOutcomeKind.Published, null, "{}", " ", ObservedAt));
        Assert.Throws<ArgumentException>(() => TrendCandidateOutcomeObservation.Create(
            Guid.NewGuid(), TrendCandidateOutcomeKind.Published, null, "{bad", "Manual", ObservedAt));
        Assert.Throws<ArgumentException>(() => TrendCandidateOutcomeObservation.Create(
            Guid.NewGuid(), TrendCandidateOutcomeKind.Published, null, "[]", "Manual", ObservedAt));
    }
}
