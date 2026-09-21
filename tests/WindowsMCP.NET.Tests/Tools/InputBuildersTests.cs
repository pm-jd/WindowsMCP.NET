using WindowsMcpNet.Native;
using WindowsMcpNet.Tools;
using Xunit;

namespace WindowsMcpNet.Tests.Tools;

public class InputBuildersTests
{
    private const ushort VkReturn = 0x0D;
    private const ushort VkTab = 0x09;

    [Fact]
    public void BuildTextInputs_Newline_BecomesEnterKeystroke()
    {
        var inputs = InputFactory.BuildTextInputs("a\nb");

        Assert.Equal(6, inputs.Length);
        Assert.Equal('a', (char)inputs[0].U.ki.wScan);
        Assert.Equal(VkReturn, inputs[2].U.ki.wVk);
        Assert.Equal(0u, inputs[2].U.ki.dwFlags & User32.KEYEVENTF_UNICODE);
        Assert.Equal(VkReturn, inputs[3].U.ki.wVk);
        Assert.NotEqual(0u, inputs[3].U.ki.dwFlags & User32.KEYEVENTF_KEYUP);
        Assert.Equal('b', (char)inputs[4].U.ki.wScan);
    }

    [Fact]
    public void BuildTextInputs_CrLf_IsSingleEnter()
    {
        var inputs = InputFactory.BuildTextInputs("\r\n");

        Assert.Equal(2, inputs.Length);
        Assert.All(inputs, i => Assert.Equal(VkReturn, i.U.ki.wVk));
    }

    [Fact]
    public void BuildTextInputs_Tab_BecomesTabKeystroke()
    {
        var inputs = InputFactory.BuildTextInputs("\t");

        Assert.Equal(2, inputs.Length);
        Assert.Equal(VkTab, inputs[0].U.ki.wVk);
    }

    [Theory]
    [InlineData("ctrl+c", new ushort[] { 0x11, 0x43 })]
    [InlineData("ctrl+shift+s", new ushort[] { 0x11, 0x10, 0x53 })]
    [InlineData("alt+f4", new ushort[] { 0x12, 0x73 })]
    [InlineData("ctrl++", new ushort[] { 0x11, 0xBB })]
    [InlineData("ctrl+plus", new ushort[] { 0x11, 0xBB })]
    [InlineData("ctrl+-", new ushort[] { 0x11, 0xBD })]
    [InlineData("win+printscreen", new ushort[] { 0x5B, 0x2C })]
    [InlineData("ctrl+numpad1", new ushort[] { 0x11, 0x61 })]
    [InlineData("shift+F12", new ushort[] { 0x10, 0x7B })]
    public void ParseShortcut_MapsNamesAndSymbols(string shortcut, ushort[] expected)
    {
        Assert.Equal(expected, InputTools.ParseShortcut(shortcut));
    }

    [Theory]
    [InlineData("ctrl+bogus")]
    [InlineData("")]
    [InlineData("+")]
    public void ParseShortcut_UnknownKey_Throws(string shortcut)
    {
        Assert.Throws<ArgumentException>(() => InputTools.ParseShortcut(shortcut));
    }
}
