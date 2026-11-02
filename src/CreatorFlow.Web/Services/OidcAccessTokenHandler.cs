using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;

namespace CreatorFlow.Web.Services;

/// <summary>Forwards the server-held OIDC token and selected workspace to the API, including Blazor circuits.</summary>
public sealed class OidcAccessTokenHandler(
    IHttpContextAccessor accessor,
    AuthenticationStateProvider authenticationStateProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        var principal = context?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var token = context is not null
                ? await context.GetTokenAsync("access_token")
                : principal.FindFirst("creatorflow_access_token")?.Value;
            token ??= principal.FindFirst("creatorflow_access_token")?.Value;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var tenantId = principal.FindFirst("creatorflow_tenant")?.Value;
            if (Guid.TryParse(tenantId, out var parsed) && parsed != Guid.Empty)
                request.Headers.TryAddWithoutValidation("X-CreatorFlow-Tenant", parsed.ToString("D"));
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
