using System.Net;
using System.Net.Sockets;

namespace BingoBongo;

/// <summary>
/// Lets the UI (served on port 8080) call the API when it is loaded from this
/// machine or from another device on the local network, but not from anywhere else.
/// </summary>
public static class LanCorsPolicy
{
    public const int UiPort = 8080;

    public static bool IsAllowedOrigin(string? origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.Port != UiPort) return false;
        if (uri.IsLoopback) return true;
        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IsPrivate(ip);
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal;

        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }
}
