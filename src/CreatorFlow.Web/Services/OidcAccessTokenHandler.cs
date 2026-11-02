using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace CreatorFlow.Web.Services;

/// <summary>Forwards server-held OIDC access tokens and the selected workspace to the API.</summary>
public sealed class OidcAccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated == true)
        {
            var token = await context.GetTokenAsync("access_token");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var tenantId = context.User.FindFirst("creatorflow_tenant")?.Value;
            if (Guid.TryParse(tenantId, out var parsed) && parsed != Guid.Empty)
                request.Headers.TryAddWithoutValidation("X-CreatorFlow-Tenant", parsed.ToString("D"));
        }
        return await base.SendAsync(request, cancellationToken);
    }
}
