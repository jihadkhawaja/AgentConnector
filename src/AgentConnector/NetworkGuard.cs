using System.Net;
using System.Net.Sockets;

namespace AgentConnector;

public static class NetworkGuard
{
    public static void ValidateHttpsUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
            throw new ConnectorException("unsafe_endpoint", "Only HTTPS endpoints on port 443 without user info or fragments are allowed.");
        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && !IsPublicAddress(address))
            throw new ConnectorException("unsafe_endpoint", "Private and reserved network destinations are not allowed.");
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 &&
                !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
                !(bytes[0] == 169 && bytes[1] == 254) &&
                !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
                !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0 || (bytes[1] == 88 && bytes[2] == 99))) &&
                !(bytes[0] == 198 && (bytes[1] is 18 or 19 || (bytes[1] == 51 && bytes[2] == 100))) &&
                !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20 &&
            !(bytes[0] == 0x3f && bytes[1] == 0xff) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
            !(bytes[0] == 0x20 && bytes[1] == 0x01 &&
              ((bytes[2] == 0x0d && bytes[3] == 0xb8) || bytes[2] < 2));
    }

    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 20,
        ConnectCallback = ConnectAsync
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        if (context.DnsEndPoint.Port != 443)
            throw new ConnectorException("unsafe_endpoint", "Only port 443 is allowed.");
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new ConnectorException("unsafe_endpoint", "DNS resolved to a private or reserved network destination.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, 443), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException) { socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new HttpRequestException("Unable to connect to the provider.");
    }
}