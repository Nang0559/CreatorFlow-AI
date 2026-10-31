using CreatorFlow.Domain.Content;
using Xunit;

namespace CreatorFlow.Domain.Tests;

public sealed class CreatorStyleProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Profile_learns_only_confirmed_winner_and_keeps_auditable_evidence()
    {
        var profile = CreatorStyleProfile.Create("creator-main", "Kênh của tôi",
            "Review sản phẩm gia dụng cho người Việt", Now);
        var experiment = CreateEvaluatedExperiment();

        var evidence = profile.LearnFrom(experiment, Now.AddDays(2));

        Assert.Equal(1, profile.EvidenceCount);
        Assert.Equal(profile.Id, evidence.CreatorStyleProfileId);
        Assert.Equal(experiment.Id, evidence.StyleExperimentId);
        Assert.Equal("Question", evidence.VariantName);
        Assert.Equal("Conversational Vietnamese narration", evidence.SpeakingStyle);
        Assert.Equal(900, evidence.Views);
        Assert.Equal(18, evidence.FollowsGained);
        Assert.Equal(3, evidence.Orders);
    }

    [Fact]
    public void Profile_rejects_unreviewed_experiment_and_duplicate_learning()
    {
        var profile = CreatorStyleProfile.Create("creator-main", "Kênh của tôi", null, Now);
        var draft = StyleExperiment.Create("Draft", "Try a hook",
            [Draft("A"), Draft("B")], Now);
        Assert.Throws<InvalidOperationException>(() => profile.LearnFrom(draft, Now));

        var evaluated = CreateEvaluatedExperiment();
        profile.LearnFrom(evaluated, Now.AddDays(2));
        Assert.Throws<InvalidOperationException>(() => profile.LearnFrom(evaluated, Now.AddDays(3)));
    }

    [Fact]
    public void Profile_guidance_is_validated_and_updated()
    {
        var profile = CreatorStyleProfile.Create("creator-main", "Initial", null, Now);
        profile.UpdateGuidance("Creator Việt", "Review chân thực", "Nói tự nhiên, mở đầu bằng câu hỏi", Now.AddHours(1));
        Assert.Equal("Creator Việt", profile.DisplayName);
        Assert.Equal("Nói tự nhiên, mở đầu bằng câu hỏi", profile.StyleSummary);
        Assert.Throws<ArgumentException>(() => profile.UpdateGuidance(" ", null, null, Now.AddHours(2)));
    }

    private static StyleExperiment CreateEvaluatedExperiment()
    {
        var experiment = StyleExperiment.Create("Hook test", "Question hook improves retention",
            [Draft("Direct"), Draft("Question")], Now);
        foreach (var variant in experiment.Variants) variant.MarkReady();
        experiment.Start(Now.AddMinutes(1));
        var variants = experiment.Variants.ToArray();
        foreach (var variant in variants) variant.MarkPublished(Now.AddHours(1));
        variants[0].RecordMetrics(700, 50, 5, 2, 2, 4, 3, 0, 6m, 20m, Now.AddDays(1));
        variants[1].RecordMetrics(900, 110, 15, 8, 9, 18, 11, 3, 10m, 20m, Now.AddDays(1));
        experiment.Evaluate(variants[1].Id, Now.AddDays(2));
        return experiment;
    }

    private static StyleVariantDraft Draft(string name) =>
        new(name, $"Hook {name}", "Conversational Vietnamese narration", "Fast cuts",
            "Original sound", $"Vietnamese script for {name}");
}
