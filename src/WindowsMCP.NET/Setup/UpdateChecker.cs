#nullable enable
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMcpNet.Setup;

public static class UpdateChecker
{
    // Injected at build time via -p:GitHubPat=...
    private const string GitHubPat = "%%GITHUB_PAT%%";
    private const string RepoOwner = "pm-jd";
    private const string RepoName = "WindowsMCP.NET";

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        });
        http.Timeout = TimeSpan.FromSeconds(30);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindowsMCP.NET", "1.0"));
        if (!string.IsNullOrEmpty(GitHubPat) && GitHubPat != "%%GITHUB_PAT%%")
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GitHubPat);
        return http;
    }

    public static async Task CheckAsync()
    {
        try
        {
            var result = await GetLatestReleaseAsync();
            if (result is { Status: UpdateStatus.UpdateAvailable })
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  Update available: v{result.Version} (current: v{GetCurrentVersion()})");
                Console.Error.WriteLine($"  Use tray icon 'Check for Updates' to install.");
                Console.Error.WriteLine();
            }
        }
        catch
        {
            // Silently ignore at startup
        }
    }

    public static async Task<UpdateCheckResult> GetLatestReleaseAsync()
    {
        if (string.IsNullOrEmpty(GitHubPat) || GitHubPat == "%%GITHUB_PAT%%")
            return UpdateCheckResult.Failed("No GitHub token configured. Rebuild with -p:GitHubPat=<token>.");

        try
        {
            using var http = CreateHttpClient();
            var response = await http.GetAsync($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            if (!response.IsSuccessStatusCode)
                return UpdateCheckResult.Failed($"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}.");

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize(json, UpdateReleaseJsonContext.Default.GitHubRelease);
            if (release is null)
                return UpdateCheckResult.Failed("Could not parse release response.");

            var latestTag = release.TagName?.TrimStart('v') ?? "";
            if (string.IsNullOrEmpty(latestTag))
                return UpdateCheckResult.Failed("Release has no version tag.");

            var currentVersion = GetCurrentVersion();
            if (!IsNewer(latestTag, currentVersion))
                return UpdateCheckResult.UpToDate(currentVersion);

            // Find the .exe asset download URL
            var exeAsset = release.Assets?.FirstOrDefault(a =>
                a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
                && a.Name.Contains("WindowsMCP", StringComparison.OrdinalIgnoreCase));

            return UpdateCheckResult.Available(latestTag, release.HtmlUrl, exeAsset?.BrowserDownloadUrl);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static async Task<bool> DownloadAndApplyUpdateAsync(string exeDownloadUrl, Action onBeforeRestart)
    {
        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "WindowsMCP.NET.exe");
        var updateExePath = currentExePath + ".update";
        var backupExePath = currentExePath + ".bak";

        try
        {
            // Step 1: Download new exe
            Console.Error.WriteLine("  Downloading update...");
            using var http = CreateHttpClient();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            var bytes = await http.GetByteArrayAsync(exeDownloadUrl);
            await File.WriteAllBytesAsync(updateExePath, bytes);
            Console.Error.WriteLine($"  Downloaded {bytes.Length / (1024 * 1024)} MB.");

            // Step 2: Create self-replacing batch script in %TEMP%
            var batPath = Path.Combine(Path.GetTempPath(), $"wmcp_update_{Guid.NewGuid():N}.bat");
            var batContent = $"""
                @echo off
                echo Updating WindowsMCP.NET...
                timeout /t 2 /nobreak >nul
                if exist "{backupExePath}" del /f "{backupExePath}"
                move /y "{currentExePath}" "{backupExePath}"
                move /y "{updateExePath}" "{currentExePath}"
                echo Update complete. Starting new version...
                start "" "{currentExePath}"
                del "%~f0"
                """;
            await File.WriteAllTextAsync(batPath, batContent);

            // Step 3: Launch bat and exit
            Console.Error.WriteLine("  Restarting with new version...");
            onBeforeRestart();

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Environment.Exit(0);
            return true; // unreachable but needed for compiler
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Update failed: {ex.Message}");
            // Cleanup
            if (File.Exists(updateExePath)) File.Delete(updateExePath);
            return false;
        }
    }

    private static string GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        if (infoVersion.Contains('+')) infoVersion = infoVersion[..infoVersion.IndexOf('+')];
        if (infoVersion is not "" and not "1.0.0") return infoVersion;

        // Fallback: version.txt
        using var stream = asm.GetManifestResourceStream("WindowsMcpNet.version.txt");
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Trim();
        }
        return "0.0.0";
    }

    private static bool IsNewer(string latest, string current)
    {
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

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

public enum UpdateStatus { UpToDate, UpdateAvailable, CheckFailed }

public sealed class UpdateCheckResult
{
    public UpdateStatus Status { get; init; }
    public string? Version { get; init; }
    public string? PageUrl { get; init; }
    public string? ExeUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate(string currentVersion) => new()
        { Status = UpdateStatus.UpToDate, Version = currentVersion };

    public static UpdateCheckResult Available(string version, string? pageUrl, string? exeUrl) => new()
        { Status = UpdateStatus.UpdateAvailable, Version = version, PageUrl = pageUrl, ExeUrl = exeUrl };

    public static UpdateCheckResult Failed(string error) => new()
        { Status = UpdateStatus.CheckFailed, ErrorMessage = error };
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubAsset>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class UpdateReleaseJsonContext : JsonSerializerContext;
