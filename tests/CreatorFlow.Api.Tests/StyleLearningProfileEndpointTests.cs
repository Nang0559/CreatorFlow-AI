using System.Net.Http.Headers;
using System.Text.Json;
using CreatorFlow.Domain.Content;
using CreatorFlow.Domain.Identity;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CreatorFlow.Api.Tests;

public sealed class StyleLearningProfileEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Learning_analysis_endpoint_uses_only_the_selected_profiles_evidence()
    {
        using var factory = new CreatorFlowApiFactory();
        var profile = CreatorStyleProfile.Create("endpoint-profile", "Endpoint profile", "Review gia dụng", Now);
        var included = CreateEvaluatedExperiment("Included profile evidence", "UNIQUE_PROFILE_EVIDENCE_HOOK");
        var unrelated = CreateEvaluatedExperiment("Unrelated tenant experiment", "UNRELATED_TENANT_HOOK");
        profile.LearnFrom(included, Now.AddMinutes(1));

        await factory.SeedAsync(async db =>
        {
            db.CreatorStyleProfiles.Add(profile);
            db.StyleExperiments.AddRange(included, unrelated);
            await db.SaveChangesAsync();
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", CreatorFlowApiFactory.ApiKey);
        var response = await client.GetAsync($"/api/v1/style-profiles/{profile.Id}/learning-analysis?minimumViewsPerVariant=100");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("eligibleExperiments").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("eligibleVariants").GetInt32());
        Assert.DoesNotContain("UNRELATED_TENANT_HOOK", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Learning_analysis_returns_not_found_for_profile_owned_by_another_tenant()
    {
        using var factory = new CreatorFlowApiFactory();
        var foreignTenantId = Guid.NewGuid();
        var foreignProfile = CreatorStyleProfile.Create("foreign-profile", "Foreign profile", "Other workspace", Now);

        await factory.SeedAsync(async (db, tenantContext) =>
        {
            var foreignTenant = Tenant.Create("Foreign workspace", "foreign-workspace", Now);
            db.Entry(foreignTenant).Property(x => x.Id).CurrentValue = foreignTenantId;
            db.Tenants.Add(foreignTenant);
            await db.SaveChangesAsync();

            tenantContext.Set(foreignTenantId, "Owner");
            db.CreatorStyleProfiles.Add(foreignProfile);
            await db.SaveChangesAsync();
        });

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CreatorFlow-Api-Key", CreatorFlowApiFactory.ApiKey);
        var response = await client.GetAsync($"/api/v1/style-profiles/{foreignProfile.Id}/learning-analysis");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static StyleExperiment CreateEvaluatedExperiment(string name, string hook)
    {
        var experiment = StyleExperiment.Create(name, "Profile endpoint integration test",
            [
                new StyleVariantDraft("A", hook, "Vietnamese narration", "Fast cuts", "Original sound", "Script A"),
                new StyleVariantDraft("B", $"Control {hook}", "Vietnamese narration", "Slow cuts", "Original sound", "Script B")
            ], Now, topic: "Home appliances");
        foreach (var variant in experiment.Variants) variant.MarkReady();
        experiment.Start(Now.AddMinutes(1));
        var variants = experiment.Variants.ToArray();
        foreach (var variant in variants) variant.MarkPublished(Now.AddHours(1));
        variants[0].RecordMetrics(700, 50, 5, 2, 2, 4, 3, 0, 6m, 20m, Now.AddDays(1));
        variants[1].RecordMetrics(900, 110, 15, 8, 9, 18, 11, 3, 10m, 20m, Now.AddDays(1));
        experiment.Evaluate(variants[1].Id, Now.AddDays(2));
        return experiment;
    }
}
