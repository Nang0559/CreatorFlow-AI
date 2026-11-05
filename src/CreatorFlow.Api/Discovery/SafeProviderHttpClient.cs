using System.Net;
using System.Net.Sockets;

namespace CreatorFlow.Api.Discovery;

/// <summary>
/// Outbound transport for tenant-configured search providers. DNS is resolved and
/// validated at connection time, and the validated address is the address actually
/// connected to (preventing DNS-rebinding between validation and connect).
/// </summary>
public static class SafeProviderHttpClient
{
    internal const int MaxResponseBytes = 2 * 1024 * 1024;

    public static HttpClient Create(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectCallback = ConnectToPublicAddressAsync
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
    }

    public static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
            throw new InvalidOperationException("Provider response vượt quá giới hạn 2 MB.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaxResponseBytes)
                throw new InvalidOperationException("Provider response vượt quá giới hạn 2 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
            addresses = [literal];
        else
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);

        // Fail closed if DNS returns even one non-public address.
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("Provider host không được phép truy cập địa chỉ mạng nội bộ.");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
                if (ex is OperationCanceledException) throw;
            }
        }
        throw new HttpRequestException("Không thể kết nối provider.", lastError);
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None) ||
            address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0]; var b = bytes[1]; var c = bytes[2];
            if (a == 0 || a == 10 || a == 127 || a >= 224) return false;
            if (a == 100 && b is >= 64 and <= 127) return false; // shared address space
            if (a == 169 && b == 254) return false;
            if (a == 172 && b is >= 16 and <= 31) return false;
            if (a == 192 && (b == 168 || (b == 0 && c == 0) || (b == 0 && c == 2) ||
                (b == 88 && c == 99) || (b == 168 && c == 0))) return false;
            if (a == 198 && (b == 18 || b == 19 || (b == 51 && c == 100))) return false;
            if (a == 203 && b == 0 && c == 113) return false;
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Only global-unicast 2000::/3 is allowed; documentation space is excluded.
            if ((bytes[0] & 0xE0) != 0x20) return false;
            if (bytes[0] == 0x20 && bytes[1] == 0x01 &&
                bytes[2] == 0x0D && bytes[3] == 0xB8) return false;
            return true;
        }
        return false;
    }
}
