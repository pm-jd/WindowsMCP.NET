#nullable enable
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMcpNet.Setup;

public static class UpdateChecker
{
    // Injected at build time via -p:GitHubPat=...
    // Falls back to empty string (update check skipped)
    private const string GitHubPat = "%%GITHUB_PAT%%";
    private const string RepoOwner = "pm-jd";
    private const string RepoName = "WindowsMCP.NET";

    public static async Task CheckAsync()
    {
        if (string.IsNullOrEmpty(GitHubPat) || GitHubPat == "%%GITHUB_PAT%%")
            return;

        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            using var http = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            });
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindowsMCP.NET", currentVersion));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GitHubPat);

            var response = await http.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize(json, UpdateReleaseJsonContext.Default.GitHubRelease);
            if (release is null) return;

            var latestTag = release.TagName?.TrimStart('v') ?? "";
            if (string.IsNullOrEmpty(latestTag)) return;

            if (IsNewer(latestTag, currentVersion))
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  Update available: v{latestTag} (current: v{currentVersion})");
                Console.Error.WriteLine($"  Download: {release.HtmlUrl}");
                Console.Error.WriteLine();
            }
        }
        catch
        {
            // Silently ignore update check failures
        }
    }

    public static async Task<(string? Version, string? Url)?> GetLatestAsync()
    {
        if (string.IsNullOrEmpty(GitHubPat) || GitHubPat == "%%GITHUB_PAT%%")
            return null;

        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindowsMCP.NET", currentVersion));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GitHubPat);

            var response = await http.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize(json, UpdateReleaseJsonContext.Default.GitHubRelease);
            if (release is null) return null;

            var latestTag = release.TagName?.TrimStart('v') ?? "";
            if (IsNewer(latestTag, currentVersion))
                return (latestTag, release.HtmlUrl);
        }
        catch { }

        return null;
    }

    private static bool IsNewer(string latest, string current)
    {
        // CalVer comparison: 2026.03.2 vs 2026.03.1
        var latestParts = latest.Split('.');
        var currentParts = current.Split('.');

        for (int i = 0; i < Math.Min(latestParts.Length, currentParts.Length); i++)
        {
            if (int.TryParse(latestParts[i], out var lp) && int.TryParse(currentParts[i], out var cp))
            {
                if (lp > cp) return true;
                if (lp < cp) return false;
            }
        }
        return false;
    }
}

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class UpdateReleaseJsonContext : JsonSerializerContext;
