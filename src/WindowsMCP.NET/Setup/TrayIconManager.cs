using System.Drawing;
using System.Windows.Forms;

namespace WindowsMcpNet.Setup;

public sealed partial class TrayIconManager : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly string _url;
    private readonly Action _onExit;

    public TrayIconManager(string url, Action onExit)
    {
        _url = url;
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
        // Run on STA thread
        var thread = new Thread(() =>
        {
            Clipboard.SetText(_url);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
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
