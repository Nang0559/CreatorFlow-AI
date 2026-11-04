using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;

namespace CreatorFlow.Web.Services;

/// <summary>
/// Refreshes an expired/near-expiry server-held OIDC access token before forwarding API requests.
/// The API still validates issuer, audience, signature and lifetime; no validation is disabled.
/// </summary>
public sealed class OidcAccessTokenHandler(
    IHttpContextAccessor accessor,
    AuthenticationStateProvider authenticationStateProvider,
    IHttpClientFactory clients,
    IOptionsMonitor<OpenIdConnectOptions> oidcOptions) : DelegatingHandler
{
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        var principal = context?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var identity = principal.Identity as ClaimsIdentity;
            var token = context is not null ? await context.GetTokenAsync("access_token") : null;
            token ??= principal.FindFirst("creatorflow_access_token")?.Value;
            var refreshToken = context is not null ? await context.GetTokenAsync("refresh_token") : null;
            refreshToken ??= principal.FindFirst("creatorflow_refresh_token")?.Value;
            var expiryText = context is not null ? await context.GetTokenAsync("expires_at") : null;
            expiryText ??= principal.FindFirst("creatorflow_access_token_expires_at")?.Value;

            if (identity is not null && !string.IsNullOrWhiteSpace(refreshToken) &&
                DateTimeOffset.TryParse(expiryText, out var expiresAt) &&
                expiresAt <= DateTimeOffset.UtcNow.Add(RefreshBeforeExpiry))
            {
                var refreshed = await RefreshAsync(refreshToken, cancellationToken);
                token = refreshed.AccessToken;
                Replace(identity, "creatorflow_access_token", refreshed.AccessToken);
                Replace(identity, "creatorflow_access_token_expires_at", refreshed.ExpiresAt.ToString("O"));
                if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    Replace(identity, "creatorflow_refresh_token", refreshed.RefreshToken);

                // Persist refreshed tokens into the auth ticket whenever this is a normal HTTP request.
                if (context is not null)
                {
                    var auth = await context.AuthenticateAsync("Cookies");
                    if (auth.Succeeded && auth.Properties is not null)
                    {
                        auth.Properties.UpdateTokenValue("access_token", refreshed.AccessToken);
                        auth.Properties.UpdateTokenValue("expires_at", refreshed.ExpiresAt.ToString("O"));
                        if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                            auth.Properties.UpdateTokenValue("refresh_token", refreshed.RefreshToken);
                        await context.SignInAsync("Cookies", auth.Principal!, auth.Properties);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var tenantId = principal.FindFirst("creatorflow_tenant")?.Value;
            if (Guid.TryParse(tenantId, out var parsed) && parsed != Guid.Empty)
                request.Headers.TryAddWithoutValidation("X-CreatorFlow-Tenant", parsed.ToString("D"));
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<RefreshResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var options = oidcOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await options.ConfigurationManager!.GetConfigurationAsync(ct);
        var endpoint = configuration.TokenEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("OIDC discovery document không cung cấp token_endpoint.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!
        });
        using var response = await clients.CreateClient("OidcBackchannel").PostAsync(endpoint, content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Không thể làm mới phiên đăng nhập OIDC (HTTP {(int)response.StatusCode}). Vui lòng đăng nhập lại; API không nới lỏng kiểm tra token.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var access = root.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(access))
            throw new InvalidOperationException("IdP không trả access_token khi làm mới phiên.");
        var seconds = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 300;
        var nextRefresh = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        return new RefreshResult(access, nextRefresh, DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, seconds)));
    }

    private static void Replace(ClaimsIdentity identity, string type, string value)
    {
        foreach (var claim in identity.FindAll(type).ToArray()) identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(type, value));
    }

    private sealed record RefreshResult(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
}
