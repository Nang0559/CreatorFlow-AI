using System.Text.Json;

namespace CreatorFlow.Api.Discovery;

/// <summary>
/// Single policy for headers that a workspace may attach to a custom discovery provider.
/// It is applied both when the provider is saved and again immediately before each outbound
/// request, so configuration persisted earlier (or supplied through appsettings) cannot bypass it.
/// </summary>
public static class ProviderHeaderPolicy
{
    private const int MaxHeaderNameLength = 100;
    private const int MaxHeaderValueLength = 2000;

    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Content-Length", "Content-Type", "Transfer-Encoding",
        "Proxy-Authorization", "Proxy-Connection", "Cookie", "Set-Cookie", "Upgrade",
        "TE", "Trailer", "Expect", "Forwarded", "X-Forwarded-For", "X-Forwarded-Host",
        "X-Forwarded-Proto", "X-Real-IP"
    };

    /// <summary>Returns true when <paramref name="name"/> is a valid, permitted HTTP header name.</summary>
    public static bool IsAllowedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxHeaderNameLength) return false;
        foreach (var c in name)
        {
            var token = (c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z') || (c is >= '0' and <= '9') ||
                        "!#$%&'*+-.^_`|~".Contains(c);
            if (!token) return false;
        }
        return !Blocked.Contains(name);
    }

    /// <summary>
    /// Validates the additional-headers JSON object and the API key header name.
    /// Returns a user-facing error message, or null when everything is acceptable.
    /// </summary>
    public static string? Validate(string? headersJson, string? apiKeyHeader)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyHeader) && !IsAllowedName(apiKeyHeader.Trim()))
            return "Tên header xác thực không hợp lệ hoặc bị cấm.";

        if (string.IsNullOrWhiteSpace(headersJson)) return null;
        try
        {
            using var headers = JsonDocument.Parse(headersJson);
            if (headers.RootElement.ValueKind != JsonValueKind.Object)
                return "Headers phải là JSON object với các giá trị chuỗi.";
            foreach (var header in headers.RootElement.EnumerateObject())
            {
                if (header.Value.ValueKind != JsonValueKind.String)
                    return "Headers phải là JSON object với các giá trị chuỗi.";
                if (!IsAllowedName(header.Name))
                    return $"Header '{Truncate(header.Name)}' không hợp lệ hoặc bị cấm.";
                var value = header.Value.GetString() ?? "";
                if (value.Length > MaxHeaderValueLength || value.Any(c => c is (< ' ' and not '\t') or '\u007f'))
                    return $"Giá trị của header '{Truncate(header.Name)}' không hợp lệ.";
            }
        }
        catch (JsonException)
        {
            return "Headers phải là JSON hợp lệ.";
        }
        return null;
    }

    private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "…";
}
