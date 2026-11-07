using System.Net;
using CreatorFlow.Api.Discovery;
using Xunit;

namespace CreatorFlow.Api.Tests.Discovery;

public sealed class SafeProviderHttpClientTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.10")]
    [InlineData("198.51.100.10")]
    [InlineData("203.0.113.10")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:192.168.1.10")]
    [InlineData("2001:db8::1")]
    [InlineData("2001::1")]            // Teredo / IETF protocol assignments
    [InlineData("2002:c0a8:0101::1")]  // 6to4 embedding 192.168.1.1
    [InlineData("2002:0a00:0001::1")]  // 6to4 embedding 10.0.0.1
    [InlineData("3fff::1")]            // documentation
    [InlineData("64:ff9b::a00:1")]     // NAT64 embedding 10.0.0.1
    [InlineData("::ffff:169.254.169.254")]
    public void Rejects_non_public_addresses(string value)
    {
        Assert.False(SafeProviderHttpClient.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Allows_public_unicast_addresses(string value)
    {
        Assert.True(SafeProviderHttpClient.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public async Task Rejects_response_larger_than_two_megabytes()
    {
        using var content = new StringContent(new string('x', SafeProviderHttpClient.MaxResponseBytes + 1));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SafeProviderHttpClient.ReadBoundedAsync(content, CancellationToken.None));
    }

    [Fact]
    public async Task Reads_response_within_size_limit()
    {
        using var content = new StringContent("ok");
        var bytes = await SafeProviderHttpClient.ReadBoundedAsync(content, CancellationToken.None);
        Assert.Equal("ok", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("https://localhost/search")]
    [InlineData("https://127.0.0.1/search")]
    [InlineData("https://[::1]/search")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://10.1.2.3/search")]
    [InlineData("https://intranet/search")]
    [InlineData("https://printer.local/search")]
    [InlineData("https://db.internal/search")]
    public void Flags_obviously_internal_hosts(string url)
    {
        Assert.True(SafeProviderHttpClient.IsObviouslyInternalHost(new Uri(url)));
    }

    [Theory]
    [InlineData("https://api.langsearch.com/v1/web-search")]
    [InlineData("https://search.example.org/api")]
    [InlineData("https://8.8.8.8/search")]
    public void Does_not_flag_public_hosts(string url)
    {
        Assert.False(SafeProviderHttpClient.IsObviouslyInternalHost(new Uri(url)));
    }
}
