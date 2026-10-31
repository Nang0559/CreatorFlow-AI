using CreatorFlow.Domain.Content;
using CreatorFlow.Domain.Discovery;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class VietnamTrendAndStyleLabTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Vietnam_trend_records_channel_video_or_sound_and_observed_metrics()
    {
        var trend = VietnamTrendSignal.Create(TrendSignalKind.Sound, TrendPlatform.TikTokVietnam,
            "sound-123", "Nhạc đang lên", "Creator A", "https://www.tiktok.com/music/sound-123",
            "Làm đẹp", "Original sound", "Artist", 10000, 800, 40, 120, 60, Now.AddDays(-2), Now);

        Assert.Equal(TrendSignalKind.Sound, trend.Kind);
        Assert.Equal(TrendPlatform.TikTokVietnam, trend.Platform);
        Assert.Equal(10000, trend.Views);
        trend.RecordObservation(15000, 1200, 55, 180, 90, Now.AddHours(1));
        Assert.Equal(15000, trend.Views);
    }

    [Fact]
    public void Trend_rejects_negative_metrics_and_non_https_urls()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VietnamTrendSignal.Create(
            TrendSignalKind.Video, TrendPlatform.TikTokVietnam, "v1", "Title", "Creator",
            "https://tiktok.com/video/1", null, null, null, -1, 0, 0, 0, 0, null, Now));
        Assert.Throws<ArgumentException>(() => VietnamTrendSignal.Create(
            TrendSignalKind.Video, TrendPlatform.TikTokVietnam, "v1", "Title", "Creator",
            "http://tiktok.com/video/1", null, null, null, 0, 0, 0, 0, 0, null, Now));
    }

    [Fact]
    public void Trend_observations_must_not_move_backwards()
    {
        var trend = VietnamTrendSignal.Create(TrendSignalKind.CreatorChannel, TrendPlatform.TikTokVietnam,
            "creator-1", "Creator", "Creator", "https://www.tiktok.com/@creator", null, null, null,
            100, 10, 1, 2, 3, null, Now);
        Assert.Throws<InvalidOperationException>(() => trend.RecordObservation(90, 9, 1, 1, 1, Now.AddMinutes(-1)));
    }

    [Fact]
    public void Sound_trend_requires_sound_name()
    {
        Assert.Throws<ArgumentException>(() => VietnamTrendSignal.Create(TrendSignalKind.Sound,
            TrendPlatform.TikTokVietnam, "s1", "Sound", "Creator", "https://tiktok.com/music/s1",
            null, null, null, 0, 0, 0, 0, 0, null, Now));
    }

    [Fact]
    public void Experiment_requires_two_or_three_distinct_complete_variants()
    {
        Assert.Throws<ArgumentException>(() => StyleExperiment.Create("A/B", "Test hooks",
            [Draft("A")], Now));
        Assert.Throws<ArgumentException>(() => StyleExperiment.Create("A/B", "Test hooks",
            [Draft("A"), Draft("a")], Now));
    }

    [Fact]
    public void Experiment_compares_three_variants_and_requires_all_measurements_before_winner()
    {
        var experiment = StyleExperiment.Create("Hook test", "Conversational hook improves retention",
            [Draft("Direct"), Draft("Story"), Draft("Question")], Now);
        foreach (var variant in experiment.Variants) variant.MarkReady();
        experiment.Start(Now.AddMinutes(1));

        var variants = experiment.Variants.ToArray();
        foreach (var variant in variants) variant.MarkPublished(Now.AddHours(1));
        variants[0].RecordMetrics(1000, 100, 10, 5, 5, 12, 8, 2, 8m, 20m, Now.AddDays(1));
        Assert.Throws<InvalidOperationException>(() => experiment.Evaluate(variants[0].Id, Now.AddDays(2)));

        variants[1].RecordMetrics(1200, 80, 12, 4, 6, 10, 6, 1, 7m, 20m, Now.AddDays(1));
        variants[2].RecordMetrics(900, 110, 15, 8, 9, 18, 11, 3, 10m, 20m, Now.AddDays(1));
        experiment.Evaluate(variants[2].Id, Now.AddDays(2));

        Assert.Equal(StyleExperimentStatus.Evaluated, experiment.Status);
        Assert.Equal(variants[2].Id, experiment.WinningVariantId);
        Assert.Equal(15.777777777777777777777777778m, variants[2].EngagementRate);
    }

    [Fact]
    public void Variant_metrics_require_publication_and_reject_impossible_watch_time()
    {
        var experiment = StyleExperiment.Create("Test", "Compare delivery",
            [Draft("A"), Draft("B")], Now);
        var variant = experiment.Variants.First();
        Assert.Throws<InvalidOperationException>(() => variant.RecordMetrics(1, 0, 0, 0, 0, 0, 0, 0, null, null, Now));
        variant.MarkReady();
        variant.MarkPublished(Now.AddHours(1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            variant.RecordMetrics(1, 0, 0, 0, 0, 0, 0, 0, 21m, 20m, Now.AddDays(1)));
    }

    private static StyleVariantDraft Draft(string name) =>
        new(name, $"Hook {name}", "Natural Vietnamese narration", "Fast cuts every 2 seconds",
            "Original or licensed trend sound", $"Vietnamese script for {name}");
}
