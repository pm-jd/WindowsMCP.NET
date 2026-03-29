using System.ComponentModel;
using System.Net.Http;
using ModelContextProtocol.Server;
using ReverseMarkdown;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class ScrapeTools
{
    private const int MaxChars = 50_000;

    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            { "User-Agent", "WindowsMCP.NET/0.1 (web scraper)" },
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
    [Description("Fetch a URL and return its content as Markdown. Truncates at 50,000 characters.")]
    public static async Task<string> Scrape(
        [Description("URL to fetch (http or https)")] string url,
        [Description("Return raw HTML instead of Markdown")] bool rawHtml = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException($"Invalid URL: '{url}'. Must be http or https.");
        }

        using var response = await _httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        if (rawHtml)
        {
            return html.Length > MaxChars
                ? html[..MaxChars] + $"\n\n[Truncated at {MaxChars:N0} characters]"
                : html;
        }

        var markdown = _markdownConverter.Convert(html);

        if (markdown.Length > MaxChars)
            markdown = markdown[..MaxChars] + $"\n\n[Truncated at {MaxChars:N0} characters]";

        return markdown;
    }
}
