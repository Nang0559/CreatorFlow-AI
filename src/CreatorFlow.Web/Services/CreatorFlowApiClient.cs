using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Json;

namespace CreatorFlow.Web.Services;

public sealed class CreatorFlowApiClient(
    HttpClient http,
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider scopedServices)
{
    public bool IsConfigured => http.BaseAddress is not null;

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    public async Task<(byte[] Content, string FileName, string ContentType)> GetDownloadAsync(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsByteArrayAsync(ct);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                       response.Content.Headers.ContentDisposition?.FileName ??
                       "creatorflow-download.zip";
        return (content, fileName.Trim('"'), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public async Task<VideoUploadResponse?> UploadVideoAsync(Stream stream, string fileName, string? rightsNote, bool confirmRights, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", Path.GetFileName(fileName));
        form.Add(new StringContent(confirmRights ? "true" : "false"), "confirmRights");
        if (!string.IsNullOrWhiteSpace(rightsNote)) form.Add(new StringContent(rightsNote), "rightsNote");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/video/assets") { Content = form };
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<VideoUploadResponse>(cancellationToken: ct);
    }

    public async Task<HttpResponseMessage> GetRawResponseAsync(string path, string? range = null, CancellationToken ct = default)
    {
        EnsureConfigured();
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(range))
            request.Headers.TryAddWithoutValidation("Range", range);
        await AddIdentityHeadersAsync(request);
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        finally { request.Dispose(); }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
    }

    public async Task PostNoContentAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PutNoContentAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private async Task AddIdentityHeadersAsync(HttpRequestMessage request)
    {
        var context = httpContextAccessor.HttpContext;
        var principal = context?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            var provider = scopedServices.GetService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>();
            if (provider is not null)
                principal = (await provider.GetAuthenticationStateAsync()).User;
        }

        if (principal?.Identity?.IsAuthenticated != true) return;
        var token = context is not null ? await context.GetTokenAsync("access_token") : null;
        token ??= principal.FindFirst("creatorflow_access_token")?.Value;
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var tenantId = principal.FindFirst("creatorflow_tenant")?.Value;
        if (Guid.TryParse(tenantId, out var parsed) && parsed != Guid.Empty)
            request.Headers.TryAddWithoutValidation("X-CreatorFlow-Tenant", parsed.ToString("D"));
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("CreatorFlow API chưa được cấu hình. Hãy thiết lập CreatorFlowApi:BaseUrl và xác thực OIDC hoặc API key bootstrap trong Development.");
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

public sealed record AiLearningSynthesisResponse(string ProposedStyleSummary, string[] RecurringStrengths,
    string[] ConflictingEvidence, string[] NextExperiments, string[] CitedExperimentIds,
    string Uncertainty, bool RequiresHumanReview);

public sealed record VideoUploadResponse(Guid Id, string FileName, long SizeBytes, string Sha256, bool Duplicate = false);
public sealed record VideoAssetItem(Guid Id,string FileName,long SizeBytes,DateTimeOffset CreatedAt,string? RightsNote);
public sealed record VideoRenderJobItem(Guid Id,Guid AssetId,string StyleKey,string Title,string Status,byte ProgressPercent,string? ErrorMessage,DateTimeOffset CreatedAt,DateTimeOffset? CompletedAt,decimal? DurationSeconds,int? Width,int? Height,string? Sha256);
