using System.Net;
using System.Net.Sockets;

namespace Bagly.Api.Services;

/// <summary>Truncates a client IP address before it's ever written to the database, so analytics
/// storage never retains an exact, individually-identifying address.</summary>
public static class IpPrivacy
{
    /// <summary>Zeroes the last IPv4 octet (e.g. 203.0.113.42 → 203.0.113.0) or the trailing
    /// 64 bits of an IPv6 address. Returns null for unparseable input.</summary>
    public static string? Mask(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || !IPAddress.TryParse(ipAddress, out var parsed))
        {
            return null;
        }

        var bytes = parsed.GetAddressBytes();

        if (parsed.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            bytes[3] = 0;
            return new IPAddress(bytes).ToString();
        }

        if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            for (var i = 8; i < bytes.Length; i++)
            {
                bytes[i] = 0;
            }

            return new IPAddress(bytes).ToString();
        }

        return null;
    }

    /// <summary>True for loopback, RFC1918/RFC4193 private ranges, and link-local addresses —
    /// these never resolve to a meaningful public location, so lookups are skipped entirely.</summary>
    public static bool IsPrivateOrLocal(this IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,
                127 => true,
                172 => bytes[1] is >= 16 and <= 31,
                192 => bytes[1] == 168,
                169 => bytes[1] == 254,
                _ => false,
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IPAddress.IPv6Loopback.Equals(ip))
            {
                return true;
            }

            // fc00::/7 — unique local addresses
            var bytes = ip.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc;
        }

        return false;
    }
}
