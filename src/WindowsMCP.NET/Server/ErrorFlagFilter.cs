using ModelContextProtocol.Protocol;

namespace WindowsMcpNet.Server;

/// <summary>
/// CallTool filter that converts the legacy "[ERROR] ..." string prefix into a proper
/// CallToolResult with IsError=true, so MCP clients can detect failures via the protocol
/// flag instead of string-matching the response body.
///
/// Tools currently swallow exceptions inside try/catch and return "[ERROR] {Type}: {message}"
/// because raw exceptions get masked by generic SDK messages. This filter complements that
/// pattern: the text stays human-readable, and IsError surfaces the failure to the protocol
/// layer additively.
/// </summary>
public static class ErrorFlagFilter
{
    public const string ErrorPrefix = "[ERROR]";

    public static CallToolResult Apply(CallToolResult result)
    {
        if (result.IsError == true) return result;
        if (result.Content is null || result.Content.Count == 0) return result;

        bool anyErrorBlock = false;
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text && IsErrorText(text.Text))
            {
                anyErrorBlock = true;
                break;
            }
        }

        if (!anyErrorBlock) return result;

        return new CallToolResult
        {
            Content = result.Content,
            StructuredContent = result.StructuredContent,
            IsError = true,
        };
    }

    private static bool IsErrorText(string? text) =>
        text is not null && text.StartsWith(ErrorPrefix, StringComparison.Ordinal);
}
