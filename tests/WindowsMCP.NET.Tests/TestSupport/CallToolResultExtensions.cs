using ModelContextProtocol.Protocol;

namespace WindowsMcpNet.Tests.TestSupport;

public static class CallToolResultExtensions
{
    /// <summary>All text blocks of a result joined with newlines — what a text-only client sees.</summary>
    public static string Text(this CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
}
