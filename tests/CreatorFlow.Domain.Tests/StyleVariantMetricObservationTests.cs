using CreatorFlow.Domain.Content;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class StyleVariantMetricObservationTests
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Capture_preserves_an_immutable_metric_snapshot_and_derived_rates()
    {
        var variant = CreatePublishedVariant();
        var observedAt = PublishedAt.AddDays(1);
        variant.RecordMetrics(1000, 100, 20, 10, 20, 30, 40, 5, 8m, 20m, observedAt);

        var observation = StyleVariantMetricObservation.Capture(variant, observedAt);

        Assert.Equal(variant.Id, observation.StyleExperimentVariantId);
        Assert.Equal(1000, observation.Views);
        Assert.Equal(15m, observation.EngagementRate);
        Assert.Equal(3m, observation.FollowRate);
        Assert.Equal(4m, observation.ProductClickRate);
        Assert.Equal(0.5m, observation.OrderRate);
        Assert.Equal(40m, observation.WatchRetentionRate);
    }

    [Fact]
    public void Published_variant_can_be_observed_again_only_at_a_later_time()
    {
        var variant = CreatePublishedVariant();
        var firstAt = PublishedAt.AddDays(1);
        variant.RecordMetrics(100, 10, 1, 1, 1, 1, 2, 0, 5m, 20m, firstAt);
        var first = StyleVariantMetricObservation.Capture(variant, firstAt);

        var secondAt = PublishedAt.AddDays(7);
        variant.RecordMetrics(500, 60, 5, 4, 8, 12, 20, 2, 9m, 20m, secondAt);
        var second = StyleVariantMetricObservation.Capture(variant, secondAt);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(100, first.Views);
        Assert.Equal(500, second.Views);
        Assert.Equal(firstAt, first.ObservedAt);
        Assert.Equal(secondAt, second.ObservedAt);
    }

    [Fact]
    public void Capture_rejects_a_timestamp_that_does_not_match_current_measurement()
    {
        var variant = CreatePublishedVariant();
        var measuredAt = PublishedAt.AddDays(1);
        variant.RecordMetrics(100, 10, 1, 1, 1, 1, 2, 0, 5m, 20m, measuredAt);

        Assert.Throws<InvalidOperationException>(() =>
            StyleVariantMetricObservation.Capture(variant, measuredAt.AddSeconds(1)));
    }

    private static StyleExperimentVariant CreatePublishedVariant()
    {
        var experiment = StyleExperiment.Create("Metric history", "Compare measured outcomes",
            [
                new StyleVariantDraft("A", "Question", "Natural", "Fast", "Original", "Script A"),
                new StyleVariantDraft("B", "Story", "Warm", "Slow", "Original", "Script B")
            ], PublishedAt);
        var variant = experiment.Variants.First();
        variant.MarkReady();
        variant.MarkPublished(PublishedAt);
        return variant;
    }
}
