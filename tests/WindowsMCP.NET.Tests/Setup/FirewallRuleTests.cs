using WindowsMcpNet.Setup;
using Xunit;

namespace WindowsMcpNet.Tests.Setup;

public class FirewallRuleTests
{
    [Fact]
    public void BuildFirewallRuleArguments_NoAllowlist_OpensPortForAnySource()
    {
        var args = SetupWizard.BuildFirewallRuleArguments(8000, []);

        Assert.Contains("localport=8000", args);
        Assert.Contains("protocol=TCP", args);
        Assert.DoesNotContain("remoteip", args);
    }

    [Fact]
    public void BuildFirewallRuleArguments_WithAllowlist_ScopesRemoteIp()
    {
        var args = SetupWizard.BuildFirewallRuleArguments(9000, ["10.0.0.1", "192.168.10.0/24"]);

        Assert.Contains("localport=9000", args);
        Assert.Contains("remoteip=10.0.0.1,192.168.10.0/24", args);
    }
}
