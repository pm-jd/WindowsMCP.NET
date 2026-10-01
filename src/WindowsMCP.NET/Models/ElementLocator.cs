using System.Security.Cryptography;
using System.Text;

namespace WindowsMcpNet.Models;

/// <summary>
/// One step of an <see cref="ElementLocator"/> path: the control's type, its identifying key
/// (AutomationId if non-empty, else Name), and its position among earlier siblings that share
/// the same (ControlType, Key) — disambiguating otherwise-identical siblings.
/// </summary>
public sealed record LocatorStep(string ControlType, string Key, int Index);

/// <summary>
/// Identifies a UI element by an ancestor path from its top-level window, rooted at the owning
/// process and window class. Stable across observations as long as the UI tree shape and the
/// element's AutomationId/Name/position among like siblings don't change; RuntimeIds are
/// deliberately not used since some UIA proxies (e.g. WinForms) regenerate them.
/// </summary>
public sealed record ElementLocator(string Process, string WindowClass, IReadOnlyList<LocatorStep> Path)
{
    private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>Value equality on <see cref="Path"/> (sequence, not reference) so two locators
    /// built independently from the same UI tree compare equal.</summary>
    public bool Equals(ElementLocator? other) =>
        other is not null
        && Process == other.Process
        && WindowClass == other.WindowClass
        && Path.SequenceEqual(other.Path);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Process);
        hash.Add(WindowClass);
        foreach (var step in Path)
            hash.Add(step);
        return hash.ToHashCode();
    }

    /// <summary>The canonical locator string the ids are hashed from:
    /// "Process|WindowClass|ControlType:Key:Index/ControlType:Key:Index/…". Two elements of one
    /// observation with the same canonical string sit at the same path in two windows of the same
    /// process name and class.</summary>
    public string Canonical()
    {
        var canonical = new StringBuilder()
            .Append(Process).Append('|')
            .Append(WindowClass).Append('|');

        for (var i = 0; i < Path.Count; i++)
        {
            if (i > 0)
                canonical.Append('/');
            var step = Path[i];
            canonical.Append(step.ControlType).Append(':').Append(step.Key).Append(':').Append(step.Index);
        }

        return canonical.ToString();
    }

    /// <summary>
    /// Short deterministic fingerprint: "e" + the first <paramref name="length"/> characters of
    /// a base32-encoded SHA-256 hash over <see cref="Canonical"/>.
    /// </summary>
    public string Id(int length) => HashId(Canonical(), length);

    /// <summary>
    /// Fingerprint that also covers the window: hashed over <see cref="Canonical"/> + "|hwnd:" +
    /// <paramref name="windowHandle"/>. Used only to tell apart elements whose locators are identical
    /// within one observation (same path in two same-class windows); the handle is otherwise not part
    /// of an element's identity.
    /// </summary>
    public string Id(int length, nint windowHandle) => HashId($"{Canonical()}|hwnd:{windowHandle}", length);

    private static string HashId(string canonical, int length)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "e" + Base32(hash)[..length];
    }

    private static string Base32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Base32Alphabet[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);

        return sb.ToString();
    }
}
