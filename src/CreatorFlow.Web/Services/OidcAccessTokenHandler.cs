using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace CreatorFlow.Web.Services;

/// <summary>
/// Làm mới OIDC access token trước khi chuyển tiếp API request.
/// Tenant header chỉ là bộ chọn workspace; API vẫn phải xác minh membership.
/// </summary>
public sealed class OidcAccessTokenHandler(
    IHttpContextAccessor accessor,
    AuthenticationStateProvider authenticationStateProvider,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions) : DelegatingHandler
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(90);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        var principal = context?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            principal = (await authenticationStateProvider
                .GetAuthenticationStateAsync()).User;
        }

        if (principal?.Identity?.IsAuthenticated != true)
            return await base.SendAsync(request, cancellationToken);

        var accessToken = context is not null
            ? await context.GetTokenAsync("access_token")
            : null;
        accessToken ??= principal.FindFirst("creatorflow_access_token")?.Value;

        var refreshToken = context is not null
            ? await context.GetTokenAsync("refresh_token")
            : null;
        refreshToken ??= principal.FindFirst("creatorflow_refresh_token")?.Value;

        var expiresAt = context is not null
            ? await context.GetTokenAsync("expires_at")
            : null;
        expiresAt ??= principal.FindFirst("creatorflow_access_token_expires_at")?.Value;

        if (IsExpiring(expiresAt))
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException(
                    "Phiên đăng nhập đã hết hạn hoặc thiếu refresh token. Vui lòng đăng nhập lại.");
            }

            var refreshed = await RefreshAsync(refreshToken, cancellationToken);

            accessToken = refreshed.AccessToken;

            // Một số Identity Provider không trả refresh_token mới.
            // Khi đó phải giữ refresh token hiện tại.
            refreshToken = string.IsNullOrWhiteSpace(refreshed.RefreshToken)
                ? refreshToken
                : refreshed.RefreshToken;

            expiresAt = DateTimeOffset.UtcNow
                .AddSeconds(refreshed.ExpiresIn)
                .ToString("O");

            UpdateClaim(principal, "creatorflow_access_token", accessToken);
            UpdateClaim(principal, "creatorflow_refresh_token", refreshToken);
            UpdateClaim(principal, "creatorflow_access_token_expires_at", expiresAt);

            if (context is not null && !context.Response.HasStarted)
            {
                await PersistCookieTokensAsync(
                    context, accessToken, refreshToken, expiresAt);
            }
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }

        // Chỉ chuyển tenant đã được chọn. API vẫn kiểm tra issuer,
        // subject và membership đang hoạt động.
        var tenantId = principal.FindFirst("creatorflow_tenant")?.Value;

        if (Guid.TryParse(tenantId, out var parsedTenantId) &&
            parsedTenantId != Guid.Empty)
        {
            request.Headers.Remove("X-CreatorFlow-Tenant");
            request.Headers.TryAddWithoutValidation(
                "X-CreatorFlow-Tenant",
                parsedTenantId.ToString("D"));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static bool IsExpiring(string? value)
    {
        return !DateTimeOffset.TryParse(value, out var expiry) ||
               expiry <= DateTimeOffset.UtcNow.Add(RefreshSkew);
    }

    private async Task<TokenRefreshResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var manager = options.ConfigurationManager
    ?? throw new InvalidOperationException(
        "OIDC ConfigurationManager chưa được cấu hình.");

    var oidcConfiguration = await manager.GetConfigurationAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(oidcConfiguration.TokenEndpoint))
        {
            throw new InvalidOperationException(
                "Không lấy được token endpoint từ OIDC discovery.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new InvalidOperationException(
                "Thiếu OIDC ClientId, không thể làm mới phiên đăng nhập.");
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = options.ClientId,
            ["refresh_token"] = refreshToken
        };

        if (!string.IsNullOrWhiteSpace(options.ClientSecret))
            form["client_secret"] = options.ClientSecret;

        var backchannel = httpClientFactory.CreateClient("OidcBackchannel");

        using var content = new FormUrlEncodedContent(form);
        using var response = await backchannel.PostAsync(
            oidcConfiguration.TokenEndpoint,
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Không thể làm mới phiên OIDC (HTTP {(int)response.StatusCode}). " +
                "Vui lòng đăng nhập lại.");
        }

        var result = await response.Content.ReadFromJsonAsync<TokenRefreshResponse>(
            cancellationToken: cancellationToken);

        if (result is null ||
            string.IsNullOrWhiteSpace(result.AccessToken) ||
            result.ExpiresIn <= 0)
        {
            throw new InvalidOperationException(
                "Identity Provider trả về dữ liệu refresh token không hợp lệ.");
        }

        return result;
    }

    private static async Task PersistCookieTokensAsync(
        HttpContext context,
        string accessToken,
        string refreshToken,
        string expiresAt)
    {
        var ticket = await context.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

        if (!ticket.Succeeded ||
            ticket.Principal is null ||
            ticket.Properties is null)
        {
            return;
        }

        UpdateClaim(ticket.Principal, "creatorflow_access_token", accessToken);
        UpdateClaim(ticket.Principal, "creatorflow_refresh_token", refreshToken);
        UpdateClaim(ticket.Principal, "creatorflow_access_token_expires_at", expiresAt);

        var tokens = ticket.Properties.GetTokens()
            .ToDictionary(token => token.Name, token => token.Value);

        tokens["access_token"] = accessToken;
        tokens["refresh_token"] = refreshToken;
        tokens["expires_at"] = expiresAt;

        ticket.Properties.StoreTokens(tokens.Select(token =>
            new AuthenticationToken
            {
                Name = token.Key,
                Value = token.Value
            }));

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ticket.Principal,
            ticket.Properties);
    }

    private static void UpdateClaim(
        ClaimsPrincipal principal,
        string type,
        string value)
    {
        if (principal.Identity is ClaimsIdentity identity)
            UpdateClaim(identity, type, value);
    }

    private static void UpdateClaim(
        ClaimsIdentity identity,
        string type,
        string value)
    {
        foreach (var claim in identity.FindAll(type).ToArray())
            identity.RemoveClaim(claim);

        identity.AddClaim(new Claim(type, value));
    }

    private sealed class TokenRefreshResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
