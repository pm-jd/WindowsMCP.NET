using WindowsMcpNet.Tests.TestSupport;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class ProcessGuardTests
{
    [Theory]
    [InlineData(0, "Idle")]
    [InlineData(4, "System")]
    [InlineData(700, "csrss")]
    [InlineData(800, "wininit")]
    [InlineData(900, "winlogon")]
    [InlineData(1000, "lsass")]
    [InlineData(1100, "services")]
    [InlineData(1200, "LSASS")]
    public void IsProtected_CriticalSystemProcesses_ReturnsTrue(int pid, string name)
    {
        Assert.True(ProcessGuard.IsProtected(pid, name));
    }

    [Fact]
    public void IsProtected_OwnProcess_ReturnsTrue()
    {
        Assert.True(ProcessGuard.IsProtected(Environment.ProcessId, "anything"));
    }

    [Theory]
    [InlineData(4242, "notepad")]
    [InlineData(4243, "chrome")]
    [InlineData(4244, "powershell")]
    public void IsProtected_OrdinaryProcesses_ReturnsFalse(int pid, string name)
    {
        Assert.False(ProcessGuard.IsProtected(pid, name));
    }

    [Fact]
    public void ProcessKill_SystemPid_IsRefused()
    {
        var result = SystemTools.ProcessTool(ProcessMode.Kill, pid: 4, force: true);

        Assert.True(result.IsError);
        Assert.Contains("protected", result.Text(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProcessKill_OwnProcess_IsRefused()
    {
        var result = SystemTools.ProcessTool(ProcessMode.Kill, pid: Environment.ProcessId);

        Assert.True(result.IsError);
        Assert.Contains("protected", result.Text(), StringComparison.OrdinalIgnoreCase);
    }
}
