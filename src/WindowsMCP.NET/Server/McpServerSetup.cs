using ModelContextProtocol.Server;

namespace WindowsMcpNet.Server;

/// <summary>Central MCP server registration shared by the stdio and HTTP hosts.</summary>
public static class McpServerSetup
{
    /// <summary>
    /// Sent to every client in the initialize response, so any MCP client learns the efficient
    /// workflow — not only Claude Code sessions that happen to read this repository's CLAUDE.md.
    /// </summary>
    public const string Instructions = """
        WindowsMCP.NET drives a Windows desktop remotely. Efficient workflow:
        - Start with Context (default: active window + screenshot). Include "ui_tree" to get numbered element labels, then target labels in Click/Type/Perform instead of raw coordinates.
        - Batch UI steps with Perform (click, type, shortcut, scroll, move, wait in one call). It returns per-step results plus a screenshot.
        - Focus or start an application with App(mode="ensure", name="<process name>"); App(mode="status") only checks. No screenshot round-trip needed.
        - List tools (FileSystem list/search, Process list, Registry list) paginate with offset/limit and report has_more/next_offset. Pass format="json" for machine-readable output.
        - Prefer the FileSystem/Registry/Process tools over PowerShell for simple operations. PowerShell output is capped at 1,000,000 characters.
        - Failures return isError=true with text starting "[ERROR] <ExceptionType>: <message>".
        """;

    public static IMcpServerBuilder AddWindowsMcpServer(this IServiceCollection services, string version)
    {
#pragma warning disable IL2026 // WithToolsFromAssembly relies on reflection; this app is neither trimmed nor AOT-compiled.
        return services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "WindowsMCP.NET", Version = version };
                o.ServerInstructions = Instructions;
            })
            .WithToolsFromAssembly()
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(next => async (ctx, ct) =>
                    ErrorFlagFilter.Apply(await next(ctx, ct)));
            });
#pragma warning restore IL2026
    }
}
