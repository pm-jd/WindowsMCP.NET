using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace WindowsMcpNet.Tools;

[McpServerToolType]
public static class NotificationTools
{
    [McpServerTool(Name = "Notification", Destructive = false, OpenWorld = false, ReadOnly = false)]
    [Description("Show a Windows toast notification via PowerShell.")]
    public static async Task<string> Notification(
        [Description("Notification title")] string title,
        [Description("Notification message body")] string message)
    {
        try
        {
            // Escape single quotes for embedding in PowerShell here-string
            var safeTitle   = title.Replace("'", "''");
            var safeMessage = message.Replace("'", "''");

            // Use BurntToast-free approach: Windows.UI.Notifications via PowerShell
            var script = new StringBuilder();
            script.Append("[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]|Out-Null;");
            script.Append("[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime]|Out-Null;");
            script.Append($"$xml=New-Object Windows.Data.Xml.Dom.XmlDocument;");
            script.Append($"$xml.LoadXml('<toast duration=\"short\"><visual><binding template=\"ToastGeneric\"><text>{EscapeXml(safeTitle)}</text><text>{EscapeXml(safeMessage)}</text></binding></visual></toast>');");
            script.Append("$toast=New-Object Windows.UI.Notifications.ToastNotification($xml);");
            script.Append("[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('WindowsMCP.NET').Show($toast);");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script.ToString().Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

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

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&apos;");
}
