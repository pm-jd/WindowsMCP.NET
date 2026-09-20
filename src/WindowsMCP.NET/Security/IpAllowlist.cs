using System.Net;
using System.Net.Sockets;

namespace WindowsMcpNet.Security;

/// <summary>
/// Parsed IP allowlist: plain addresses or CIDR ranges, IPv4 and IPv6.
/// IPv4-mapped IPv6 client addresses (<c>::ffff:a.b.c.d</c>, what a dual-stack
/// listener reports for IPv4 clients) are normalised to IPv4 so a plain
/// <c>a.b.c.d</c> entry matches them. An empty list allows everyone.
/// </summary>
public sealed class IpAllowlist
{
    private readonly List<(IPAddress Network, int PrefixLength)> _rules = [];

    public IpAllowlist(IEnumerable<string> entries)
    {
        foreach (var entry in entries)
            _rules.Add(ParseRule(entry));
    }

    public bool IsEmpty => _rules.Count == 0;

    public bool Allows(IPAddress? address)
    {
        if (IsEmpty) return true;
        if (address is null) return false;

        var client = Normalize(address);
        foreach (var (network, prefixLength) in _rules)
        {
            if (network.AddressFamily == client.AddressFamily && InRange(client, network, prefixLength))
                return true;
        }
        return false;
    }

    private static (IPAddress Network, int PrefixLength) ParseRule(string entry)
    {
        var slash = entry.IndexOf('/');
        var addressPart = (slash < 0 ? entry : entry[..slash]).Trim();
        if (addressPart.Length == 0 || !IPAddress.TryParse(addressPart, out var address))
            throw new ArgumentException(
                $"Invalid IP allowlist entry '{entry}'. Expected an IP address or a CIDR range like 10.0.0.0/24.");

        address = Normalize(address);
        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefixLength = maxPrefix;
        if (slash >= 0 &&
            (!int.TryParse(entry[(slash + 1)..], out prefixLength) || prefixLength < 0 || prefixLength > maxPrefix))
        {
            throw new ArgumentException($"Invalid CIDR prefix in IP allowlist entry '{entry}' (0-{maxPrefix}).");
        }

        return (address, prefixLength);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool InRange(IPAddress candidate, IPAddress network, int prefixLength)
    {
        var c = candidate.GetAddressBytes();
        var n = network.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        if (!c.AsSpan(0, fullBytes).SequenceEqual(n.AsSpan(0, fullBytes)))
            return false;

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0)
            return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (c[fullBytes] & mask) == (n[fullBytes] & mask);
    }
}
