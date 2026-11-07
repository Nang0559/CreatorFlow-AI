using CreatorFlow.Api.Discovery;
using Xunit;

namespace CreatorFlow.Api.Tests.Discovery;

public sealed class ProviderHeaderPolicyTests
{
    [Theory]
    [InlineData("Host")]
    [InlineData("host")]
    [InlineData("Content-Length")]
    [InlineData("Content-Type")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Cookie")]
    [InlineData("X-Forwarded-For")]
    [InlineData("Bad Header")]
    [InlineData("Bad:Header")]
    [InlineData("")]
    public void Rejects_forbidden_or_malformed_header_names(string name)
    {
        Assert.False(ProviderHeaderPolicy.IsAllowedName(name));
        Assert.NotNull(ProviderHeaderPolicy.Validate(null, name.Length == 0 ? "bad name" : name));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-API-Key")]
    [InlineData("Ocp-Apim-Subscription-Key")]
    public void Allows_normal_authentication_header_names(string name)
    {
        Assert.True(ProviderHeaderPolicy.IsAllowedName(name));
        Assert.Null(ProviderHeaderPolicy.Validate(null, name));
    }

    [Fact]
    public void Rejects_forbidden_name_inside_additional_headers()
    {
        Assert.NotNull(ProviderHeaderPolicy.Validate("{\"Host\":\"internal.example\"}", null));
    }

    [Fact]
    public void Rejects_non_string_and_non_object_headers()
    {
        Assert.NotNull(ProviderHeaderPolicy.Validate("{\"X-Test\":1}", null));
        Assert.NotNull(ProviderHeaderPolicy.Validate("[\"X-Test\"]", null));
        Assert.NotNull(ProviderHeaderPolicy.Validate("not-json", null));
    }

    [Fact]
    public void Rejects_control_characters_in_header_values()
    {
        Assert.NotNull(ProviderHeaderPolicy.Validate("{\"X-Test\":\"a\\r\\nInjected: yes\"}", null));
    }

    [Fact]
    public void Accepts_well_formed_additional_headers()
    {
        Assert.Null(ProviderHeaderPolicy.Validate("{\"X-Client\":\"creatorflow\",\"Accept-Language\":\"vi\"}", "X-API-Key"));
        Assert.Null(ProviderHeaderPolicy.Validate(null, null));
        Assert.Null(ProviderHeaderPolicy.Validate("   ", "  "));
    }
}
