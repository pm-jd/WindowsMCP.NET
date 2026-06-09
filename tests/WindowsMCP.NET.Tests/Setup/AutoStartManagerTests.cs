using System.Xml.Linq;
using WindowsMcpNet.Setup;
using Xunit;

namespace WindowsMcpNet.Tests.Setup;

public class AutoStartManagerTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    [Fact]
    public void BuildTaskXml_IsWellFormed_AndHardened()
    {
        var xml = AutoStartManager.BuildTaskXml(
            @"C:\Promicron\Tools\WindowsMCP.NET.exe", "S-1-5-21-1-2-3-1001");

        var doc = XDocument.Parse(xml); // throws if malformed

        // Battery gating disabled — the latent USV/EFEM landmine that silently
        // blocks the task on any PC Windows reports as "on battery".
        Assert.Equal("false", doc.Descendants(Ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.Equal("false", doc.Descendants(Ns + "StopIfGoingOnBatteries").Single().Value);

        // No execution time limit; catch up if a logon trigger was missed.
        Assert.Equal("PT0S", doc.Descendants(Ns + "ExecutionTimeLimit").Single().Value);
        Assert.Equal("true", doc.Descendants(Ns + "StartWhenAvailable").Single().Value);

        // Trigger / action / principal wiring.
        Assert.Single(doc.Descendants(Ns + "LogonTrigger"));
        Assert.Equal(@"C:\Promicron\Tools\WindowsMCP.NET.exe",
            doc.Descendants(Ns + "Command").Single().Value);
        Assert.Equal("S-1-5-21-1-2-3-1001", doc.Descendants(Ns + "UserId").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(Ns + "RunLevel").Single().Value);
    }

    [Fact]
    public void BuildTaskXml_EscapesSpecialCharactersInPath()
    {
        // A '&' in the path must be XML-escaped or schtasks gets malformed XML.
        var xml = AutoStartManager.BuildTaskXml(@"C:\Tools\A&B\app.exe", "S-1-5-18");

        var doc = XDocument.Parse(xml); // would throw if '&' were not escaped
        Assert.Equal(@"C:\Tools\A&B\app.exe", doc.Descendants(Ns + "Command").Single().Value);
    }

    [Fact]
    public void RunElevatedCommand_UnknownArg_ReturnsFailure_WithoutSideEffects()
    {
        // The dispatch guard must reject unknown verbs (exit 1) and never touch
        // the task store — only the two known verbs perform registry/schtasks work.
        Assert.Equal(1, AutoStartManager.RunElevatedCommand("--bogus"));
    }
}
