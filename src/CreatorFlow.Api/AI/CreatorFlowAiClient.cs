using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CreatorFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

public sealed class CreatorFlowAiClient(
    HttpClient http, IConfiguration configuration, CreatorFlowDbContext? db,
    ITenantContext tenant, IDataProtectionProvider protection)
{
    public async Task<IResult> ForwardAsync<TRequest>(string path, TRequest payload, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["CREATORFLOW_AI_BASE_URL"];
        var serviceKey = configuration["CREATORFLOW_AI_API_KEY"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Length < 32)
            return Results.Problem("CreatorFlow AI service is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)) ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            return Results.Problem("CreatorFlow AI base URL must be HTTPS (HTTP is allowed only for localhost).", statusCode: 503);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri.ToString().TrimEnd('/') + path))
        { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("X-CreatorFlow-AI-Key", serviceKey);

        // A workspace route is sent only over the authenticated API-to-AI service channel.
        // The browser never receives this envelope or any provider credential.
        if (db is not null && tenant.IsResolved)
        {
            var rows = await db.AIProviderConfigurations.AsNoTracking()
                .Where(x => x.IsActive).OrderBy(x => x.RoutingRole).ThenBy(x => x.FallbackOrder).ToListAsync(cancellationToken);
            var primary = rows.FirstOrDefault(x => x.RoutingRole == "Primary");
            if (primary is not null)
            {
                try
                {
                    var protector = protection.CreateProtector("CreatorFlow.AIProvider.ApiKey.v1");
                    object Convert(AIProviderConfiguration row) => new {
                        name = row.DisplayName, base_url = row.BaseUrl, model = row.Model,
                        api_key = protector.Unprotect(row.EncryptedApiKey ?? throw new InvalidOperationException("Provider credential is missing.")),
                        timeout_seconds = row.TimeoutSeconds, max_output_tokens = row.MaxOutputTokens
                    };
                    var route = new {
                        primary = Convert(primary),
                        fallbacks = rows.Where(x => x.RoutingRole == "Fallback").OrderBy(x => x.FallbackOrder).Select(Convert).ToArray()
                    };
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(route)));
                    request.Headers.TryAddWithoutValidation("X-CreatorFlow-LLM-Route", encoded);
                }
                catch (Exception)
                {
                    return Results.Problem("Workspace AI provider configuration cannot be decrypted. Check the API Data Protection key ring.", statusCode: 503);
                }
            }
        }

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Content(body, response.Content.Headers.ContentType?.ToString() ?? "application/json", statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException) { return Results.Problem("CreatorFlow AI service could not be reached.", statusCode: 503); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Results.Problem("CreatorFlow AI request timed out.", statusCode: 504); }
    }
}

public sealed record AiAnalyzeRequest(string Text, string Language = "vi", string? Topic = null,
    string? CreatorPositioning = null);
public sealed record AiGenerateVariantsRequest(string SourceAnalysis, string Topic, string CreatorPositioning,
    string StyleSummary = "", string? ProductContext = null, int VariantCount = 3, int TargetDurationSeconds = 45);
