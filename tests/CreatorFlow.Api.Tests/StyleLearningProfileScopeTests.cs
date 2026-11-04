using CreatorFlow.Api.Identity;
using CreatorFlow.Domain.Content;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class StyleLearningProfileScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Learning_analysis_input_contains_only_evaluated_experiments_linked_as_profile_evidence()
    {
        var options = new DbContextOptionsBuilder<CreatorFlowDbContext>()
            .UseInMemoryDatabase($"style-profile-scope-{Guid.NewGuid():N}")
            .Options;
        var tenant = new TenantContext();
        tenant.Set(Guid.NewGuid(), "Owner");

        var profile = CreatorStyleProfile.Create("profile-a", "Profile A", "Review gia dụng", Now);
        var evidencedExperiment = CreateEvaluatedExperiment("Evidence for A");
        var unrelatedExperiment = CreateEvaluatedExperiment("Not evidence for A");
        profile.LearnFrom(evidencedExperiment, Now.AddDays(1));

        await using (var seed = new CreatorFlowDbContext(options, tenant))
        {
            seed.CreatorStyleProfiles.Add(profile);
            seed.StyleExperiments.AddRange(evidencedExperiment, unrelatedExperiment);
            await seed.SaveChangesAsync();
        }

        await using var query = new CreatorFlowDbContext(options, tenant);
        var loadedProfile = await query.CreatorStyleProfiles.AsNoTracking()
            .Include(x => x.Evidence)
            .SingleAsync(x => x.Id == profile.Id);
        var evidenceIds = loadedProfile.Evidence.Select(x => x.StyleExperimentId).Distinct().ToArray();
        var scopedExperiments = await query.StyleExperiments.AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => evidenceIds.Contains(x.Id) && x.Status == StyleExperimentStatus.Evaluated)
            .ToArrayAsync();

        Assert.Single(scopedExperiments);
        Assert.Equal(evidencedExperiment.Id, scopedExperiments[0].Id);
        Assert.DoesNotContain(scopedExperiments, x => x.Id == unrelatedExperiment.Id);
    }

    private static StyleExperiment CreateEvaluatedExperiment(string name)
    {
        var experiment = StyleExperiment.Create(name, "Profile evidence scope",
            [Draft("A"), Draft("B")], Now);
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
        new(name, $"Hook {name}", "Vietnamese narration", "Fast cuts",
            "Original sound", $"Vietnamese script {name}");
}
