using CreatorFlow.Domain.Content;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class StyleLearningEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Analysis_finds_repeated_winning_patterns_and_keeps_topic_breakdown()
    {
        var experiments = new[]
        {
            Evaluated("Kitchen", "Question hook", "Question", "Direct"),
            Evaluated("Kitchen", "Question hook", "Question", "Story"),
            Evaluated("Beauty", "Question hook", "Question", "Direct")
        };

        var result = StyleLearningEngine.Analyze(experiments);

        Assert.Equal(3, result.EligibleExperiments);
        Assert.Equal(6, result.EligibleVariants);
        Assert.Contains(result.StrongestSignals, x =>
            x.Dimension == "Hook" && x.Value == "Question" &&
            x.SupportingExperiments == 3 && x.WinningExperiments == 3);
        Assert.Contains(result.TopicBreakdown, x => x.Topic == "Kitchen" && x.EligibleExperiments == 2);
        Assert.Contains("người sáng tạo duyệt", result.SuggestedStyleSummary);
        Assert.DoesNotContain(result.StrongestSignals, x => x.Dimension == "SpeakingStyle");
    }

    [Fact]
    public void Analysis_excludes_unmeasured_or_under_sampled_experiments()
    {
        var enough = Evaluated("Kitchen", "Enough", "Question", "Direct");
        var tooSmall = Evaluated("Kitchen", "Too small", "Question", "Direct", views: 20);

        var result = StyleLearningEngine.Analyze([enough, tooSmall], minimumViewsPerVariant: 100);

        Assert.Equal(1, result.EligibleExperiments);
        Assert.Equal(1, result.ExcludedExperiments);
    }

    [Fact]
    public void Analysis_can_be_scoped_to_a_topic_and_does_not_invent_signals_from_one_test()
    {
        var kitchen = Evaluated("Kitchen", "One", "Question", "Direct");
        var beauty = Evaluated("Beauty", "Two", "Question", "Direct");

        var result = StyleLearningEngine.Analyze([kitchen, beauty], topic: "Kitchen");

        Assert.Equal(1, result.EligibleExperiments);
        Assert.Empty(result.StrongestSignals);
        Assert.Single(result.TopicBreakdown);
        Assert.Equal("Kitchen", result.TopicBreakdown[0].Topic);
    }

    private static StyleExperiment Evaluated(string topic, string name, string winnerHook,
        string losingHook, long views = 1000)
    {
        var experiment = StyleExperiment.Create(name, "Compare opening hooks",
            [
                Draft("Winner", winnerHook),
                Draft("Loser", losingHook)
            ], Now, topic: topic);
        foreach (var variant in experiment.Variants) variant.MarkReady();
        experiment.Start(Now.AddMinutes(1));
        var variants = experiment.Variants.ToArray();
        foreach (var variant in variants) variant.MarkPublished(Now.AddHours(1));
        variants[0].RecordMetrics(views, 100, 10, 10, 10, 10, 20, 5, 8m, 20m, Now.AddDays(1));
        variants[1].RecordMetrics(views, 20, 2, 1, 1, 1, 2, 0, 3m, 20m, Now.AddDays(1));
        experiment.Evaluate(variants[0].Id, Now.AddDays(2));
        return experiment;
    }

    private static StyleVariantDraft Draft(string name, string hook) =>
        new(name, hook, "Natural Vietnamese", "Fast cuts", "Original sound", "Vietnamese script");
}
