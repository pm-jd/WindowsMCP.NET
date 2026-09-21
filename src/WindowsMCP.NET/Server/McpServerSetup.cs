using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Server;

/// <summary>Central MCP server registration shared by the stdio and HTTP hosts.</summary>
public static class McpServerSetup
{
    /// <summary>
    /// Serializer options for tool arguments and schemas. The SDK defaults carry a global
    /// <see cref="JsonStringEnumConverter"/> without a naming policy, and options-level converters
    /// take precedence over type-level <c>[JsonConverter]</c> attributes. Left in place, every tool
    /// enum would be advertised and parsed as PascalCase and inputs like "read_base64" or "ui_tree"
    /// would be rejected. Removing it lets each enum's own attribute define the wire format.
    /// Protocol enums are unaffected because they come from the SDK's source-generated context.
    /// </summary>
    public static JsonSerializerOptions ToolSerializerOptions { get; } = CreateToolSerializerOptions();

    private static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        for (var i = options.Converters.Count - 1; i >= 0; i--)
        {
            if (options.Converters[i] is JsonStringEnumConverter)
                options.Converters.RemoveAt(i);
        }
        options.MakeReadOnly();
        return options;
    }

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

    /// <summary>
    /// Desktop/UI singletons the tools receive as method parameters. Registering them is also what
    /// makes the SDK inject them instead of exposing them in the tool input schema.
    /// </summary>
    public static IServiceCollection AddWindowsMcpServices(this IServiceCollection services)
    {
        services.AddSingleton<DesktopService>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<UiAutomationService>();
        services.AddSingleton<UiTreeService>();
        return services;
    }

    public static IMcpServerBuilder AddWindowsMcpServer(this IServiceCollection services, string version)
    {
#pragma warning disable IL2026 // WithToolsFromAssembly relies on reflection; this app is neither trimmed nor AOT-compiled.
        return services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "WindowsMCP.NET", Version = version };
                o.ServerInstructions = Instructions;
            })
            .WithToolsFromAssembly(typeof(McpServerSetup).Assembly, ToolSerializerOptions)
            .WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(next => async (ctx, ct) =>
                    ErrorFlagFilter.Apply(await next(ctx, ct)));
            });
#pragma warning restore IL2026
    }
}
