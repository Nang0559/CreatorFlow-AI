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
}
