using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using ReverseMarkdown;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ScrapeTools
{
    private const int MaxChars = 50_000;

    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "WindowsMCP.NET/0.1" },
            { "Accept", "text/html,application/xhtml+xml,*/*" },
        }
    };

    // ReverseMarkdown 6.x: the Default flavor already emits GitHub-style output (pipe tables,
    // fenced code blocks, ~~strikethrough~~). Do NOT set Flavor = MarkdownFlavor.GitHub — in
    // 6.2.1 any non-default flavor returns the input HTML unchanged (covered by ScrapeToolsTests).
    private static readonly Converter _markdownConverter = new(new ReverseMarkdown.Config
    {
        Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.PassThrough },
        Formatting = { RemoveComments = true },
        Links = { SmartHref = true },
    });

    [McpServerTool(Name = "Scrape", ReadOnly = true, Idempotent = true)]
    [Description("Fetch URL and return content as Markdown.")]
    public static async Task<string> Scrape(
        [Description("URL to fetch (http or https)")] string url,
        [Description("Optional text filter: only return lines containing this string (case-insensitive)")] string? query = null,
        CancellationToken ct = default)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new ArgumentException($"Invalid URL: '{url}'. Must be http or https.");
            }

            using var response = await _httpClient.GetAsync(uri, ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            return ConvertHtml(html, query);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // client cancelled the request; the SDK reports it as cancelled, not as a tool error
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Converts HTML to GitHub-flavored Markdown, optionally keeping only lines that contain
    /// <paramref name="query"/> (case-insensitive), and caps the result at <see cref="MaxChars"/>.
    /// </summary>
    public static string ConvertHtml(string html, string? query)
    {
        var markdown = _markdownConverter.Convert(html);

        if (query is not null)
        {
            var filteredLines = markdown
                .Split('\n')
                .Where(line => line.Contains(query, StringComparison.OrdinalIgnoreCase));
            markdown = string.Join('\n', filteredLines);
        }

        if (markdown.Length > MaxChars)
            markdown = markdown[..MaxChars] +
                       $"\n\n[Truncated at {MaxChars.ToString("N0", CultureInfo.InvariantCulture)} characters]";

        return markdown;
    }
}
