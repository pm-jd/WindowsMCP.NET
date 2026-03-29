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
            contextMenu.Items.Add("Check for Updates", null, (_, _) => _ = CheckForUpdatesAsync());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Stop Server", null, (_, _) =>
            {
                _onExit();
                Application.ExitThread();
            });

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,  // Use default app icon
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

    private static async Task CheckForUpdatesAsync()
    {
        var result = await UpdateChecker.GetLatestAsync();
        if (result is (string ver, string url))
        {
            var thread = new Thread(() =>
            {
                var answer = MessageBox.Show(
                    $"Update available: v{ver}\n\nOpen download page?",
                    "WindowsMCP.NET Update",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        else
        {
            var thread = new Thread(() =>
            {
                MessageBox.Show("You are running the latest version.", "WindowsMCP.NET", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
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

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);
}
