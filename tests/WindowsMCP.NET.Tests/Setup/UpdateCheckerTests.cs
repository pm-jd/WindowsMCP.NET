using System.Security.Cryptography;
using System.Text;
using WindowsMcpNet.Setup;
using Xunit;

namespace WindowsMcpNet.Tests.Setup;

public class UpdateCheckerTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("fake exe bytes");
    private static readonly string PayloadHex = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();

    // --- VerifyChecksum: accepts the `sha256sum` file format CI publishes next to the exe ---

    [Fact]
    public void VerifyChecksum_MatchingHash_ReturnsTrue()
    {
        var checksumFile = $"{PayloadHex}  WindowsMCP.NET.exe\n";

        Assert.True(UpdateChecker.VerifyChecksum(Payload, checksumFile));
    }

    [Fact]
    public void VerifyChecksum_UppercaseHexWithoutFilename_ReturnsTrue()
    {
        Assert.True(UpdateChecker.VerifyChecksum(Payload, PayloadHex.ToUpperInvariant()));
    }

    [Fact]
    public void VerifyChecksum_MismatchedHash_ReturnsFalse()
    {
        var otherHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("tampered"))).ToLowerInvariant();

        Assert.False(UpdateChecker.VerifyChecksum(Payload, $"{otherHex}  WindowsMCP.NET.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("not-a-hash  WindowsMCP.NET.exe")]
    [InlineData("abcd")]
    public void VerifyChecksum_UnparseableContent_ReturnsFalse(string content)
    {
        Assert.False(UpdateChecker.VerifyChecksum(Payload, content));
    }

    // --- SelectAssets: picks the exe and its companion .sha256 from the release asset list ---

    [Fact]
    public void SelectAssets_FindsExeAndChecksum()
    {
        var assets = new List<GitHubAsset>
        {
            new() { Name = "WindowsMCP.NET.exe.sha256", BrowserDownloadUrl = "https://x/sha" },
            new() { Name = "WindowsMCP.NET.exe", BrowserDownloadUrl = "https://x/exe" },
        };

        var (exeUrl, shaUrl) = UpdateChecker.SelectAssets(assets);

        Assert.Equal("https://x/exe", exeUrl);
        Assert.Equal("https://x/sha", shaUrl);
    }

    [Fact]
    public void SelectAssets_NoChecksumAsset_ReturnsNullShaUrl()
    {
        var assets = new List<GitHubAsset>
        {
            new() { Name = "WindowsMCP.NET.exe", BrowserDownloadUrl = "https://x/exe" },
        };

        var (exeUrl, shaUrl) = UpdateChecker.SelectAssets(assets);

        Assert.Equal("https://x/exe", exeUrl);
        Assert.Null(shaUrl);
    }

    [Fact]
    public void SelectAssets_NullList_ReturnsNulls()
    {
        var (exeUrl, shaUrl) = UpdateChecker.SelectAssets(null);

        Assert.Null(exeUrl);
        Assert.Null(shaUrl);
    }
}
