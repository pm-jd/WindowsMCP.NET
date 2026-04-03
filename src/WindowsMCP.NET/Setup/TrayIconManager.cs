namespace WindowsMcpNet.Setup;

public sealed partial class TrayIconManager : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly string _url;
    private readonly string? _apiKey;
    private readonly Action _onExit;

    public TrayIconManager(string url, string? apiKey, Action onExit)
    {
        _url = url;
        _apiKey = apiKey;
        _onExit = onExit;
    }

    public void Show()
    {
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add($"WindowsMCP.NET — {_url}", null, null!).Enabled = false;
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Show Console", null, (_, _) => ShowConsole());
            contextMenu.Items.Add("Copy Config Snippet", null, (_, _) => CopyConfig());
            contextMenu.Items.Add("Check for Updates", null, (_, _) => CheckForUpdatesHandler());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Stop Server", null, (_, _) =>
            {
                _onExit();
                Application.ExitThread();
            });

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateTrayIcon(),
                Text = $"WindowsMCP.NET\n{_url}",
                Visible = true,
                ContextMenuStrip = contextMenu,
            };

            _notifyIcon.DoubleClick += (_, _) => ShowConsole();

            Application.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    public static void HideConsole()
    {
        var handle = GetConsoleWindow();
        if (handle != nint.Zero)
            ShowWindow(handle, 0); // SW_HIDE
    }

    private static void ShowConsole()
    {
        var handle = GetConsoleWindow();
        if (handle != nint.Zero)
            ShowWindow(handle, 5); // SW_SHOW
    }

    private void CopyConfig()
    {
        var snippet = _apiKey is not null
            ? $"claude mcp add windows-mcp-dotnet \"{_url}\" --transport http --scope user --header \"Authorization: Bearer {_apiKey}\""
            : _url;

        var thread = new Thread(() =>
        {
            Clipboard.SetText(snippet);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private void CheckForUpdatesHandler()
    {
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var result = await UpdateChecker.GetLatestReleaseAsync();

        var thread = new Thread(() =>
        {
            switch (result.Status)
            {
                case UpdateStatus.UpdateAvailable:
                    var message = result.ExeUrl is not null
                        ? $"Update available: v{result.Version}\n\nDownload and install automatically?"
                        : $"Update available: v{result.Version}\n\nOpen download page?";

                    var answer = MessageBox.Show(message, "WindowsMCP.NET Update",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (answer == DialogResult.Yes)
                    {
                        if (result.ExeUrl is not null)
                        {
                            _ = UpdateChecker.DownloadAndApplyUpdateAsync(result.ExeUrl, () =>
                            {
                                _onExit();
                                Application.ExitThread();
                            });
                        }
                        else if (result.PageUrl is not null)
                        {
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo(result.PageUrl) { UseShellExecute = true });
                        }
                    }
                    break;

                case UpdateStatus.UpToDate:
                    MessageBox.Show($"You are running the latest version (v{result.Version}).",
                        "WindowsMCP.NET", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;

                case UpdateStatus.CheckFailed:
                    MessageBox.Show($"Update check failed:\n\n{result.ErrorMessage}",
                        "WindowsMCP.NET", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Teal rounded-rectangle background
        using var bgBrush = new SolidBrush(Color.FromArgb(0, 151, 167));
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var rect = new Rectangle(1, 1, 29, 29);
        int r = 6;
        path.AddArc(rect.X, rect.Y, r, r, 180, 90);
        path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
        path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
        path.CloseFigure();
        g.FillPath(bgBrush, path);

        // White lightning bolt (automation symbol)
        using var fgBrush = new SolidBrush(Color.White);
        g.FillPolygon(fgBrush, new PointF[]
        {
            new(17, 4), new(10, 16), new(15, 16),
            new(13, 28), new(22, 14), new(17, 14),
        });

        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);
}
