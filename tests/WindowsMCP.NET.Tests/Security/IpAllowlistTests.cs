using System.Net;
using WindowsMcpNet.Security;
using Xunit;

namespace WindowsMcpNet.Tests.Security;

public class IpAllowlistTests
{
    [Fact]
    public void Allows_ExactIPv4Entry_ReturnsTrue()
    {
        var allowlist = new IpAllowlist(["10.0.0.5"]);

        Assert.True(allowlist.Allows(IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void Allows_UnlistedIPv4_ReturnsFalse()
    {
        var allowlist = new IpAllowlist(["10.0.0.5"]);

        Assert.False(allowlist.Allows(IPAddress.Parse("10.0.0.6")));
    }

    [Fact]
    public void Allows_IPv4MappedIPv6_MatchesIPv4Entry()
    {
        // Kestrel on a dual-stack "0.0.0.0"/"::" listener reports IPv4 clients as ::ffff:a.b.c.d
        var allowlist = new IpAllowlist(["10.0.0.5"]);

        Assert.True(allowlist.Allows(IPAddress.Parse("::ffff:10.0.0.5")));
    }

    [Fact]
    public void Allows_CidrRange_MatchesInsideAndRejectsOutside()
    {
        var allowlist = new IpAllowlist(["192.168.10.0/24"]);

        Assert.True(allowlist.Allows(IPAddress.Parse("192.168.10.1")));
        Assert.True(allowlist.Allows(IPAddress.Parse("192.168.10.254")));
        Assert.False(allowlist.Allows(IPAddress.Parse("192.168.11.1")));
    }

    [Fact]
    public void Allows_IPv6Entry_MatchesIPv6Client()
    {
        var allowlist = new IpAllowlist(["::1", "fd00::/8"]);

        Assert.True(allowlist.Allows(IPAddress.IPv6Loopback));
        Assert.True(allowlist.Allows(IPAddress.Parse("fd12:3456::1")));
        Assert.False(allowlist.Allows(IPAddress.Parse("fe80::1")));
    }

    [Fact]
    public void Allows_EmptyList_AllowsEveryone()
    {
        var allowlist = new IpAllowlist([]);

        Assert.True(allowlist.IsEmpty);
        Assert.True(allowlist.Allows(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public void Allows_NullAddress_WithEntries_ReturnsFalse()
    {
        var allowlist = new IpAllowlist(["10.0.0.5"]);

        Assert.False(allowlist.Allows(null));
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/abc")]
    [InlineData("")]
    public void Constructor_InvalidEntry_ThrowsArgumentException(string entry)
    {
        Assert.Throws<ArgumentException>(() => new IpAllowlist([entry]));
    }
}
