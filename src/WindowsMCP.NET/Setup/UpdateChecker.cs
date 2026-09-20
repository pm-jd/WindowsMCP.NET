#nullable enable
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
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
        if (!string.IsNullOrEmpty(GitHubPat) && !GitHubPat.StartsWith("%%"))
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
        if (string.IsNullOrEmpty(GitHubPat) || GitHubPat.StartsWith("%%"))
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

            var (exeUrl, shaUrl) = SelectAssets(release.Assets);
            return UpdateCheckResult.Available(latestTag, release.HtmlUrl, exeUrl, shaUrl);
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the release exe, verifies it against the published <c>.sha256</c> asset and
    /// swaps it in via a helper batch script. Refuses to install (fail closed) when no checksum
    /// asset exists or the hash does not match, so a tampered download can never replace the binary.
    /// </summary>
    public static async Task<bool> DownloadAndApplyUpdateAsync(string exeDownloadUrl, string? checksumDownloadUrl, Action onBeforeRestart)
    {
        if (string.IsNullOrEmpty(checksumDownloadUrl))
        {
            Console.Error.WriteLine("  Update refused: release has no .sha256 checksum asset, cannot verify integrity.");
            return false;
        }

        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? Path.Combine(AppContext.BaseDirectory, "WindowsMCP.NET.exe");
        var updateExePath = currentExePath + ".update";
        var backupExePath = currentExePath + ".bak";

        try
        {
            // Step 1: Download new exe + checksum, verify before anything touches disk
            Console.Error.WriteLine("  Downloading update...");
            using var http = CreateHttpClient();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            var bytes = await http.GetByteArrayAsync(exeDownloadUrl);
            var checksumFile = await http.GetStringAsync(checksumDownloadUrl);
            if (!VerifyChecksum(bytes, checksumFile))
            {
                Console.Error.WriteLine("  Update refused: SHA-256 of the downloaded exe does not match the published checksum.");
                return false;
            }
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

    /// <summary>
    /// Verifies <paramref name="data"/> against a <c>sha256sum</c>-style checksum file
    /// ("&lt;hex&gt;  filename"). Returns false on mismatch or unparseable content (fail closed).
    /// </summary>
    public static bool VerifyChecksum(ReadOnlySpan<byte> data, string checksumFileContent)
    {
        var expected = ParseChecksum(checksumFileContent);
        if (expected is null) return false;

        var actual = SHA256.HashData(data);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[]? ParseChecksum(string content)
    {
        var firstLine = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var token = firstLine?.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (token is null || token.Length != SHA256.HashSizeInBytes * 2) return null;

        try { return Convert.FromHexString(token); }
        catch (FormatException) { return null; }
    }

    /// <summary>Picks the WindowsMCP exe and its companion <c>.exe.sha256</c> from the release assets.</summary>
    public static (string? ExeUrl, string? ShaUrl) SelectAssets(IEnumerable<GitHubAsset>? assets)
    {
        if (assets is null) return (null, null);

        string? exeUrl = null;
        string? shaUrl = null;
        foreach (var asset in assets)
        {
            if (asset.Name is null || !asset.Name.Contains("WindowsMCP", StringComparison.OrdinalIgnoreCase))
                continue;

            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exeUrl ??= asset.BrowserDownloadUrl;
            else if (asset.Name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase))
                shaUrl ??= asset.BrowserDownloadUrl;
        }
        return (exeUrl, shaUrl);
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
    /// <summary>Download URL of the <c>.exe.sha256</c> asset; null means auto-install is not possible.</summary>
    public string? ShaUrl { get; init; }
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult UpToDate(string currentVersion) => new()
        { Status = UpdateStatus.UpToDate, Version = currentVersion };

    public static UpdateCheckResult Available(string version, string? pageUrl, string? exeUrl, string? shaUrl) => new()
        { Status = UpdateStatus.UpdateAvailable, Version = version, PageUrl = pageUrl, ExeUrl = exeUrl, ShaUrl = shaUrl };

    public static UpdateCheckResult Failed(string error) => new()
        { Status = UpdateStatus.CheckFailed, ErrorMessage = error };
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(List<GitHubAsset>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
public partial class UpdateReleaseJsonContext : JsonSerializerContext;
