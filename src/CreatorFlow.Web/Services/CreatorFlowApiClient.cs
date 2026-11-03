using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Json;

namespace CreatorFlow.Web.Services;

public sealed class CreatorFlowApiClient(
    HttpClient http,
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider scopedServices,
    IConfiguration configuration,
    IWebHostEnvironment environment)
{
    public bool IsConfigured => http.BaseAddress is not null;
    public bool CanBootstrapOwner => environment.IsDevelopment() &&
        configuration.GetValue<bool>("Authentication:OwnerBootstrapEnabled") &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:OwnerBootstrapToken"]);

    public async Task<T?> GetAsync<T>(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    public async Task<(byte[] Content, string FileName, string ContentType)> GetDownloadAsync(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
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
        await EnsureSuccessAsync(response, ct);
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
        await EnsureSuccessAsync(response, ct);
        if (response.Content.Headers.ContentLength == 0) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
    }

    public async Task PostNoContentAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteNoContentAsync(string path, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task PutNoContentAsync<TRequest>(string path, TRequest body, CancellationToken ct = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        await AddIdentityHeadersAsync(request);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task BootstrapOwnerAsync(string name, string slug, CancellationToken ct = default)
    {
        EnsureConfigured();
        if (!CanBootstrapOwner)
            throw new InvalidOperationException("Owner bootstrap UI chỉ được bật trong Development khi Authentication:OwnerBootstrapEnabled=true và secret đã cấu hình.");
        var bootstrapSecret = configuration["Authentication:OwnerBootstrapToken"];
        if (string.IsNullOrWhiteSpace(bootstrapSecret))
            throw new InvalidOperationException("Web chưa được cấu hình Authentication:OwnerBootstrapToken. Hãy cấu hình secret ở môi trường Web rồi khởi động lại container.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/owner/bootstrap")
        {
            Content = JsonContent.Create(new { name, slug })
        };
        await AddIdentityHeadersAsync(request);
        request.Headers.TryAddWithoutValidation("X-CreatorFlow-Owner-Bootstrap", bootstrapSecret);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        var detail = ExtractApiError(body);
        var status = (int)response.StatusCode;
        var reason = response.ReasonPhrase ?? "Lỗi từ máy chủ";
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"API trả về HTTP {status} ({reason})."
            : $"API trả về HTTP {status} ({reason}): {detail}";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string ExtractApiError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            var root = document.RootElement;
            var parts = new List<string>();

            if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String)
                parts.Add(detail.GetString()!);
            else if (root.TryGetProperty("title", out var title) && title.ValueKind == System.Text.Json.JsonValueKind.String)
                parts.Add(title.GetString()!);

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var property in errors.EnumerateObject())
                {
                    var messages = property.Value.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? string.Join("; ", property.Value.EnumerateArray().Select(x => x.ToString()))
                        : property.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(messages))
                        parts.Add($"{property.Name}: {messages}");
                }
            }

            return string.Join(" ", parts.Distinct()).Trim();
        }
        catch (System.Text.Json.JsonException)
        {
            return body.Length <= 1200 ? body : body[..1200] + "…";
        }
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
