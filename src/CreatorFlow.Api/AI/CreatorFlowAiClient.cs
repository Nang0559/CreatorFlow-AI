using System.Net;
using System.Net.Http.Json;

public sealed class CreatorFlowAiClient(HttpClient http, IConfiguration configuration)
{
    public async Task<IResult> ForwardAsync<TRequest>(string path, TRequest payload, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["CREATORFLOW_AI_BASE_URL"];
        var serviceKey = configuration["CREATORFLOW_AI_API_KEY"];
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Length < 32)
            return Results.Problem("CreatorFlow AI service is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps &&
             !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)) ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            return Results.Problem("CreatorFlow AI base URL must be HTTPS (HTTP is allowed only for localhost).",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri.ToString().TrimEnd('/') + path))
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("X-CreatorFlow-AI-Key", serviceKey);

        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return Results.Content(body, contentType, statusCode: (int)response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return Results.Problem("CreatorFlow AI service could not be reached.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem("CreatorFlow AI request timed out.", statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}

public sealed record AiAnalyzeRequest(string Text, string Language = "vi", string? Topic = null,
    string? CreatorPositioning = null);

public sealed record AiGenerateVariantsRequest(string SourceAnalysis, string Topic, string CreatorPositioning,
    string StyleSummary = "", string? ProductContext = null, int VariantCount = 3, int TargetDurationSeconds = 45);
