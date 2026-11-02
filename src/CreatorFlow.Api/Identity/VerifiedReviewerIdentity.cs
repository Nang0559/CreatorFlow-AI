using System.Security.Claims;
using CreatorFlow.Api.Security;

namespace CreatorFlow.Api.Identity;

public static class VerifiedReviewerIdentity
{
    public static string? Resolve(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // API-key clients are service identities, not human reviewers. Until OIDC is enabled,
        // approval/rejection must fail closed rather than accept a caller-supplied reviewer ID.
        if (principal.Identity?.AuthenticationType == ApiKeyAuthenticationHandler.SchemeName)
            return null;

        var subject = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(subject) ? null : subject;
    }
}
