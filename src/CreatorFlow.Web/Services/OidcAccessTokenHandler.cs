using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

namespace CreatorFlow.Web.Services;

/// <summary>
/// Refreshes the server-held OIDC access token before forwarding API requests.
/// Tenant selection remains a selector only; the API still verifies membership.
/// </summary>
public sealed class OidcAccessTokenHandler(
    IHttpContextAccessor accessor,
    AuthenticationStateProvider authenticationStateProvider,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : DelegatingHandler
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(90);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        var principal = context?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var accessToken = context is not null
                ? await context.GetTokenAsync("access_token")
                : null;
            accessToken ??= principal.FindFirst("creatorflow_access_token")?.Value;

            var refreshToken = context is not null
                ? await context.GetTokenAsync("refresh_token")
                : null;
            refreshToken ??= principal.FindFirst("creatorflow_refresh_token")?.Value;

            var expiresAtValue = context is not null
                ? await context.GetTokenAsync("expires_at")
                : null;
            expiresAtValue ??= principal.FindFirst("creatorflow_access_token_expires_at")?.Value;

            if (IsExpiring(expiresAtValue))
            {
                if (string.IsNullOrWhiteSpace(refreshToken))
                    throw new InvalidOperationException(
                        "Phiên đăng nhập đã hết hạn và không có refresh token. Vui lòng đăng nhập lại.");

                var refreshed = await RefreshAsync(refreshToken, cancellationToken);
                accessToken = refreshed.AccessToken;
                refreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                    ? refreshToken
                    : refreshed.RefreshToken;
                var expiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn)
                    .ToString("O");

                UpdateClaim(principal, "creatorflow_access_token", accessToken);
                UpdateClaim(principal, "creatorflow_refresh_token", refreshToken);
                UpdateClaim(principal, "creatorflow_access_token_expires_at", expiresAt);

                if (context is not null)
                    await PersistCookieTokensAsync(context, principal, accessToken, refreshToken, expiresAt);
            }

            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var tenantId = principal.FindFirst("creatorflow_tenant")?.Value;
            if (Guid.TryParse(tenantId, out var parsed) && parsed != Guid.Empty)
            {
                request.Headers.Remove("X-CreatorFlow-Tenant");
                request.Headers.TryAddWithoutValidation("X-CreatorFlow-Tenant", parsed.ToString("D"));
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsExpiring(string? value) =>
        !DateTimeOffset.TryParse(value, out var expiry) ||
        expiry <= DateTimeOffset.UtcNow.Add(RefreshSkew);

    private async Task<TokenRefreshResponse> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken)
    {
        var authority = configuration["Authentication:Oidc:Authority"]?.TrimEnd('/');
        var clientId = configuration["Authentication:Oidc:ClientId"];
        var clientSecret = configuration["Authentication:Oidc:ClientSecret"];
        if (string.IsNullOrWhiteSpace(authority) ||
            string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Thiếu cấu hình OIDC để làm mới phiên đăng nhập.");

        var backchannel = httpClientFactory.CreateClient("OidcBackchannel");
        var discovery = await backchannel.GetFromJsonAsync<OidcDiscoveryDocument>(
            $"{authority}/.well-known/openid-configuration", cancellationToken);
        if (string.IsNullOrWhiteSpace(discovery?.TokenEndpoint))
            throw new InvalidOperationException("Không lấy được token endpoint từ OIDC discovery.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken
        });
        using var response = await backchannel.PostAsync(
            discovery.TokenEndpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Không thể làm mới phiên OIDC (HTTP {(int)response.StatusCode}). Vui lòng đăng nhập lại.");

        var result = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(
            cancellationToken: cancellationToken);
        if (result is null || string.IsNullOrWhiteSpace(result.AccessToken) || result.ExpiresIn <= 0)
            throw new InvalidOperationException("Identity Provider trả về token refresh không hợp lệ.");
        return result;
    }

    private static async Task PersistCookieTokensAsync(
        HttpContext context, ClaimsPrincipal principal, string accessToken,
        string refreshToken, string expiresAt)
    {
        var ticket = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!ticket.Succeeded || ticket.Principal is null || ticket.Properties is null)
            return;

        var identity = ticket.Principal.Identity as ClaimsIdentity;
        if (identity is not null)
        {
            UpdateClaim(identity, "creatorflow_access_token", accessToken);
            UpdateClaim(identity, "creatorflow_refresh_token", refreshToken);
            UpdateClaim(identity, "creatorflow_access_token_expires_at", expiresAt);
        }

        var tokens = ticket.Properties.GetTokens().ToDictionary(x => x.Name, x => x.Value);
        tokens["access_token"] = accessToken;
        tokens["refresh_token"] = refreshToken;
        tokens["expires_at"] = expiresAt;
        ticket.Properties.StoreTokens(tokens.Select(x => new AuthenticationToken
        {
            Name = x.Key,
            Value = x.Value
        }));
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, ticket.Principal, ticket.Properties);
    }

    private static void UpdateClaim(ClaimsPrincipal principal, string type, string value)
    {
        if (principal.Identity is ClaimsIdentity identity)
            UpdateClaim(identity, type, value);
    }

    private static void UpdateClaim(ClaimsIdentity identity, string type, string value)
    {
        foreach (var claim in identity.FindAll(type).ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(type, value));
    }

    private sealed class OidcDiscoveryDocument
    {
        [JsonPropertyName("token_endpoint")]
        public string? TokenEndpoint { get; init; }
    }

    private sealed class TokenRefreshResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;
        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
