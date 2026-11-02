using System.Security.Claims;
using CreatorFlow.Api.Identity;
using CreatorFlow.Api.Security;
using Xunit;

namespace CreatorFlow.Api.Tests.Identity;

public sealed class VerifiedReviewerIdentityTests
{
    [Fact]
    public void Resolve_RejectsSharedApiKeyServiceIdentity()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "api-client")],
            ApiKeyAuthenticationHandler.SchemeName);

        Assert.Null(VerifiedReviewerIdentity.Resolve(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Resolve_UsesOidcSubjectClaim()
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", "oidc-user-123"), new Claim(ClaimTypes.NameIdentifier, "mapped-user")],
            "OpenIdConnect");

        Assert.Equal("oidc-user-123", VerifiedReviewerIdentity.Resolve(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Resolve_UsesNameIdentifierWhenSubjectIsNotMapped()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "verified-user-456")],
            "OpenIdConnect");

        Assert.Equal("verified-user-456", VerifiedReviewerIdentity.Resolve(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Resolve_RejectsAuthenticatedPrincipalWithoutStableSubject()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "display-name")], "OpenIdConnect");

        Assert.Null(VerifiedReviewerIdentity.Resolve(new ClaimsPrincipal(identity)));
    }
}
