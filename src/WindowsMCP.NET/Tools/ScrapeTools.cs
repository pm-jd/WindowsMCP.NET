using System.ComponentModel;
using System.Net.Http;
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

    private static readonly Converter _markdownConverter = new(new ReverseMarkdown.Config
    {
        UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.PassThrough,
        GithubFlavored = true,
        RemoveComments = true,
        SmartHrefHandling = true,
    });

    [McpServerTool(Name = "Scrape", ReadOnly = true, Idempotent = true)]
    [Description("Fetch a URL and return its content as Markdown. Truncates at 50,000 characters. " +
                 "Optionally filter content by a query string.")]
    public static async Task<string> Scrape(
        [Description("URL to fetch (http or https)")] string url,
        [Description("Optional text filter: only return lines containing this string (case-insensitive)")] string? query = null,
        [Description("Use browser DOM for scraping (not implemented; accepted for API compatibility)")] bool use_dom = false,
        [Description("Use sampling to reduce content length")] bool use_sampling = true)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException($"Invalid URL: '{url}'. Must be http or https.");
        }

        using var response = await _httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        var markdown = _markdownConverter.Convert(html);

        if (query is not null)
        {
            var filteredLines = markdown
                .Split('\n')
                .Where(line => line.Contains(query, StringComparison.OrdinalIgnoreCase));
            markdown = string.Join('\n', filteredLines);
        }

        if (markdown.Length > MaxChars)
            markdown = markdown[..MaxChars] + $"\n\n[Truncated at {MaxChars:N0} characters]";

        return markdown;
    }
}
