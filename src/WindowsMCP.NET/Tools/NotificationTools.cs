using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class NotificationTools
{
    // Static script — title/message arrive via env vars, never inlined into PowerShell source.
    // InnerText assignment lets the XmlDocument escape entities; no manual escaping needed.
    private const string ToastScript =
        "[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]|Out-Null;" +
        "[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]|Out-Null;" +
        "$xml=New-Object Windows.Data.Xml.Dom.XmlDocument;" +
        "$xml.LoadXml('<toast duration=\"short\"><visual><binding template=\"ToastGeneric\"><text></text><text></text></binding></visual></toast>');" +
        "$texts=$xml.GetElementsByTagName('text');" +
        "$texts.Item(0).InnerText=$env:WMCP_TOAST_TITLE;" +
        "$texts.Item(1).InnerText=$env:WMCP_TOAST_MESSAGE;" +
        "$toast=New-Object Windows.UI.Notifications.ToastNotification($xml);" +
        "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('WindowsMCP.NET').Show($toast);";

    [McpServerTool(Name = "Notification", Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("Show a Windows toast notification via PowerShell.")]
    public static async Task<string> Notification(
        [Description("Notification title")] string title,
        [Description("Notification message body")] string message)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ToastScript));

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            psi.EnvironmentVariables["WMCP_TOAST_TITLE"] = title;
            psi.EnvironmentVariables["WMCP_TOAST_MESSAGE"] = message;

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            proc.Start();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                proc.Kill();
                return "Notification timed out.";
            }

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync();
                return $"Notification may have failed (exit {proc.ExitCode}): {err.Trim()}";
            }

            return $"Notification sent: \"{title}\"";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.GetType().Name}: {ex.Message}";
        }
    }
}
