using System.Net.Http.Json;

namespace CreatorFlow.Web.Services;

public sealed class CreatorFlowApiClient(HttpClient http)
{
    public bool IsConfigured => http.BaseAddress is not null && http.DefaultRequestHeaders.Contains("X-CreatorFlow-Api-Key");

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var response = await http.PostAsJsonAsync(path, request, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
    }

    public async Task PostNoContentAsync<TRequest>(string path, TRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var response = await http.PostAsJsonAsync(path, request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutNoContentAsync<TRequest>(string path, TRequest request, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var response = await http.PutAsJsonAsync(path, request, ct);
        response.EnsureSuccessStatusCode();
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("CreatorFlow API chưa được cấu hình. Hãy thiết lập CreatorFlowApi:BaseUrl và CreatorFlowApi:ApiKey ở phía server.");
    }
}

public sealed record VietnamTrendItem(Guid Id, string Kind, string Platform, string ExternalId, string Title,
    string CreatorName, string SourceUrl, string? Topic, string? SoundName, string? SoundArtist,
    long Views, long Likes, long Comments, long Shares, long Saves, DateTimeOffset? PublishedAt, DateTimeOffset ObservedAt);

public sealed record CreatorStyleProfileItem(Guid Id, string ProfileKey, string DisplayName, string Positioning,
    string StyleSummary, int EvidenceCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record CreatorStyleEvidenceItem(Guid Id, Guid StyleExperimentId, Guid WinningVariantId,
    string ExperimentName, string VariantName, string Hook, string SpeakingStyle, string EditPacing,
    string AudioApproach, long Views, long Likes, long Comments, long Shares, long Saves,
    long FollowsGained, long ProductClicks, long Orders, decimal? AverageWatchSeconds,
    decimal? VideoDurationSeconds, decimal? EngagementRate, DateTimeOffset LearnedAt);

public sealed record CreatorStyleProfileDetail(Guid Id, string ProfileKey, string DisplayName,
    string Positioning, string StyleSummary, CreatorStyleEvidenceItem[] Evidence,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record StyleExperimentItem(Guid Id, Guid? ContentProjectId, string Name, string Hypothesis,
    string? Topic, string Status, Guid? WinningVariantId, StyleVariantItem[] Variants);

public sealed record StyleFactorInsightItem(string Dimension, string Value, int SupportingExperiments,
    int WinningExperiments, int LosingExperiments, decimal WinRate, decimal Lift);

public sealed record StyleTopicInsightItem(string Topic, int EligibleExperiments, StyleFactorInsightItem[] StrongestSignals);

public sealed record StyleLearningAnalysisItem(string? TopicFilter, int EligibleExperiments, int EligibleVariants,
    int ExcludedExperiments, decimal BaselineWinRate, StyleFactorInsightItem[] StrongestSignals,
    StyleTopicInsightItem[] TopicBreakdown, string SuggestedStyleSummary, string Methodology);

public sealed record StyleVariantItem(Guid Id, string Name, string Hook, string SpeakingStyle, string EditPacing,
    string AudioApproach, string Script, string Status, long Views, long Likes, long Comments, long Shares,
    long Saves, long FollowsGained, long ProductClicks, long Orders, decimal? AverageWatchSeconds,
    decimal? VideoDurationSeconds, decimal? EngagementRate, DateTimeOffset? PublishedAt, DateTimeOffset? MeasuredAt);


public sealed record AiAnalyzeRequest(string Text, string Language = "vi", string? Topic = null,
    string? CreatorPositioning = null);
public sealed record AiAnalyzeResponse(string Language, string Summary, string[] Topics, string AudienceIntent,
    string[] StoryMechanisms, string[] HookPatterns, string[] LocalizationOpportunities,
    string[] ElementsToAvoidCopying, bool RequiresHumanReview);
public sealed record AiGenerateVariantsRequest(string SourceAnalysis, string Topic, string CreatorPositioning,
    string StyleSummary = "", string? ProductContext = null, int VariantCount = 3, int TargetDurationSeconds = 45);
public sealed record AiGeneratedVariant(string Name, string CreativeAngle, string Hook, string SpeakingStyle,
    string EditPacing, string AudioApproach, string Script, string[] ShotPlan, string CallToAction);
public sealed record AiGenerateVariantsResponse(string Topic, string Language, AiGeneratedVariant[] Variants,
    string[] DifferentiationNotes, bool RequiresHumanReview);
