using System.Diagnostics;
using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using WindowsMcpNet.Models;
using WindowsMcpNet.Native;
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Services;

/// <summary>
/// Live UIA collector behind the <c>Observe</c> tool. Resolves the target top-level windows for a
/// scope, walks one cached subtree per window (see <see cref="ObservationBuilder"/>'s header for the
/// node-list contract this must satisfy), hit-tests visibility for the bottom-most (main) window of
/// each process, and hands the raw node list to <see cref="ObservationBuilder"/>. A window that does
/// not answer UI Automation is reported as unreadable instead of being skipped
/// (<see cref="ReadWindow"/>). Also resolves a
/// stored <see cref="ElementLocator"/> back to a live <see cref="AutomationElement"/> for verified
/// actions (Task 7).
/// </summary>
public sealed class ObservationService : IDisposable
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>Types <see cref="ObservationBuilder"/> can emit (actionable + text/caption types) —
    /// hit-testing any other type would be wasted work since it's dropped from the output anyway.</summary>
    private static readonly HashSet<string> HitTestableTypes = new(StringComparer.Ordinal)
    {
        "Button", "SplitButton", "CheckBox", "RadioButton", "ComboBox", "Edit", "Document", "Spinner",
        "Slider", "Hyperlink", "MenuItem", "TabItem", "ListItem", "TreeItem", "DataItem", "HeaderItem",
        "Text", "Group", "Header",
    };

    private readonly ILogger<ObservationService> _logger;
    private readonly UIA3Automation _automation;
    private readonly Lock _lock = new();

    /// <summary>
    /// Whether a non-root node counts at all — for the cached walk (dropped nodes, and their subtree,
    /// never enter <see cref="ObservationBuilder"/>'s node list) and for live resolution (<see cref="FindLive"/>
    /// must count the very same live siblings when matching a <see cref="LocatorStep.Index"/>, or its
    /// count would drift from the one <see cref="ObservationBuilder"/> assigned when it built the
    /// locator — e.g. an offscreen or 1px-tall twin of a kept sibling would shift every later index).
    /// </summary>
    internal static bool IsCollectible(bool isOffscreen, Rectangle rect) =>
        !isOffscreen && rect.Width > 1 && rect.Height > 1;

    /// <summary>The value the collector keeps for a node: nothing for a password field — its content
    /// must not reach the observation, the signature or a log — and nothing for an empty value.</summary>
    internal static string? CollectedValue(bool isPassword, string? rawValue) =>
        isPassword || string.IsNullOrEmpty(rawValue) ? null : rawValue;

    /// <summary>What the collector does with one window (see <see cref="ReadWindow"/>).</summary>
    internal enum WindowRead
    {
        /// <summary>UI Automation answered: the window is walked.</summary>
        Read,

        /// <summary>UI Automation did not answer: the window is reported without nodes.</summary>
        Unreadable,

        /// <summary>The window disappeared between its enumeration and the read: it is left out.</summary>
        Gone,
    }

    /// <summary>
    /// Reads one window's UIA root through <paramref name="read"/> (null = it did not answer) — unless
    /// another window of the same process has already failed to answer in this collection
    /// (<paramref name="unreadablePids"/>): an application whose UI thread is busy, e.g. inside an
    /// <c>Invoke</c> that opened a modal dialog, lets EVERY request run into the timeout, so a
    /// collection pays for one failed attempt per process and marks the rest unreadable unasked.
    /// A window that did not answer is never skipped — an observation is never silently empty — with
    /// one exception: a window that is no longer visible (<paramref name="stillVisible"/>; a menu or
    /// tooltip that closed meanwhile) is simply gone and says nothing about its process.
    /// </summary>
    internal static WindowRead ReadWindow<TRoot>(
        nint handle, uint pid, HashSet<uint> unreadablePids,
        Func<nint, TRoot?> read, Func<nint, bool> stillVisible, out TRoot? root)
        where TRoot : class
    {
        root = null;
        if (unreadablePids.Contains(pid))
            return WindowRead.Unreadable;

        root = read(handle);
        if (root is not null)
            return WindowRead.Read;

        if (!stillVisible(handle))
            return WindowRead.Gone;

        unreadablePids.Add(pid);
        return WindowRead.Unreadable;
    }

    public ObservationService(ILogger<ObservationService> logger)
    {
        _logger = logger;
        _automation = new UIA3Automation();
    }

    /// <summary>Collects the scope's windows/elements and builds the compact <see cref="Observation"/>.</summary>
    public Observation Observe(ObserveScope scope, string? process, int maxElements, CancellationToken ct)
    {
        lock (_lock)
        {
            var handles = ResolveWindowHandles(scope, process);
            var result = Collect(handles, hitTest: true, ct);
            var timings = new ObservationTimings(result.WalkMs, result.HitMs, result.WalkMs + result.HitMs);
            return ObservationBuilder.Build(result.Windows, result.Nodes, maxElements, timings, result.BudgetExceeded);
        }
    }

    /// <summary>Same collection as <see cref="Observe"/> without hit-testing — the cheap signature used
    /// for before/after change detection (verification, stall detection).</summary>
    public string Signature(ObserveScope scope, string? process, CancellationToken ct)
    {
        lock (_lock)
        {
            var handles = ResolveWindowHandles(scope, process);
            var result = Collect(handles, hitTest: false, ct);
            var timings = new ObservationTimings(result.WalkMs, 0, result.WalkMs);
            return ObservationBuilder.Build(result.Windows, result.Nodes, maxElements: 500, timings, result.BudgetExceeded).Signature;
        }
    }

    /// <summary>Signature of exactly one process instance: the visible top-level windows of
    /// <paramref name="pid"/>. Verification of element actions uses this so that, with two instances of
    /// the same app, the instance that was acted on is the one watched. Throws
    /// <see cref="InvalidOperationException"/> when the pid has no visible window.</summary>
    public string Signature(int pid, CancellationToken ct)
    {
        lock (_lock)
        {
            var handles = EnumerateVisibleTopLevelWindows((uint)pid);
            if (handles.Count == 0)
                throw new InvalidOperationException($"process {pid} has no visible window");

            var result = Collect(handles, hitTest: false, ct);
            var timings = new ObservationTimings(result.WalkMs, 0, result.WalkMs);
            return ObservationBuilder.Build(result.Windows, result.Nodes, maxElements: 500, timings, result.BudgetExceeded).Signature;
        }
    }

    /// <summary>
    /// Resolves a stored element against the live desktop and returns it together with the top-level
    /// window it was found in. The candidate windows come from <see cref="SelectResolutionWindows"/>
    /// (window affinity): the window the element was observed in when it is still there, nothing for
    /// a transient window that is gone, else every visible window of the locator's process name and
    /// class in z-order. In those windows: walk the locator path step-by-step over live control-view
    /// children; when that fails, fall back to a unique control-view descendant matching the last
    /// step's type and key. Fresh UIA calls only (no cache) — the live tree may have changed since
    /// the locator was captured.
    /// </summary>
    public (AutomationElement Element, nint Hwnd)? FindLive(StoredElement stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var locator = stored.Locator;

        lock (_lock)
        {
            var processName = StripExeSuffix(locator.Process);
            var matching = EnumerateVisibleTopLevelWindows(pid: null)
                .Where(h => GetClassName(h) == locator.WindowClass
                            && string.Equals(GetProcessName(GetPid(h)), processName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var roots = new List<(AutomationElement Root, nint Hwnd)>();
            foreach (var handle in SelectResolutionWindows(stored.WindowHandle, stored.Transient, matching))
            {
                if (TryGetRoot(handle) is { } root)
                    roots.Add((root, handle));
            }

            foreach (var (root, hwnd) in roots)
            {
                if (TryInWindow(hwnd, () => WalkPath(root, locator.Path, 0)) is { } match)
                    return (match, hwnd);
            }

            if (locator.Path.Count == 0)
                return null;

            var lastStep = locator.Path[^1];
            var candidates = new List<(AutomationElement Element, nint Hwnd)>();
            foreach (var (root, hwnd) in roots)
            {
                foreach (var candidate in TryInWindow(hwnd, () => FindDescendantsByTypeAndKey(root, lastStep)) ?? [])
                    candidates.Add((candidate, hwnd));
            }

            return candidates.Count == 1 ? candidates[0] : null;
        }
    }

    /// <summary>
    /// Window affinity of a stored id. <paramref name="matchingWindows"/> are the visible top-level
    /// windows whose process name and window class equal the locator's, in z-order.
    /// <list type="bullet">
    /// <item>The window the element was observed in is still one of them → resolve ONLY there.</item>
    /// <item>That window is gone and it was transient (popup, menu, dialog) → resolve nowhere: a
    /// remembered dialog button must not act on a later dialog of the same class.</item>
    /// <item>That window is gone and it was a main window (e.g. the application was restarted) → any
    /// same-class window of that process name, topmost first.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<nint> SelectResolutionWindows(
        nint storedHandle, bool transient, IReadOnlyList<nint> matchingWindows)
    {
        if (storedHandle != nint.Zero && matchingWindows.Contains(storedHandle))
            return [storedHandle];

        return transient ? [] : matchingWindows;
    }

    public void Dispose() => _automation.Dispose();

    // --- Target resolution (plain Win32 only — no UIA walk) ---------------------------------------

    private static List<nint> ResolveWindowHandles(ObserveScope scope, string? process)
    {
        switch (scope)
        {
            case ObserveScope.Foreground:
            {
                var foreground = User32.GetForegroundWindow();
                EnsureForegroundIsApplicationWindow(foreground);
                var pid = GetPid(foreground);
                return EnumerateVisibleTopLevelWindows(pid);
            }

            case ObserveScope.Process:
            {
                if (string.IsNullOrEmpty(process))
                    throw new ArgumentException("process is required for scope=process", nameof(process));

                var pid = FindFirstProcessPidWithVisibleWindow(process)
                          ?? throw new InvalidOperationException($"process '{process}' has no visible window");
                return EnumerateVisibleTopLevelWindows(pid);
            }

            case ObserveScope.Desktop:
                return EnumerateVisibleTopLevelWindows(pid: null);

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown ObserveScope");
        }
    }

    private static void EnsureForegroundIsApplicationWindow(nint foreground)
    {
        const string NoForegroundWindow = "no foreground application window";

        if (foreground == nint.Zero)
            throw new InvalidOperationException(NoForegroundWindow);

        var className = GetClassName(foreground);
        if (className is "Shell_TrayWnd" or "Progman" or "WorkerW")
            throw new InvalidOperationException(NoForegroundWindow);

        if (GetPid(foreground) == (uint)Environment.ProcessId)
            throw new InvalidOperationException(NoForegroundWindow);
    }

    private static uint? FindFirstProcessPidWithVisibleWindow(string processName)
    {
        var needle = StripExeSuffix(processName);
        uint? found = null;

        User32.EnumWindows((hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;

            var pid = GetPid(hWnd);
            if (string.Equals(GetProcessName(pid), needle, StringComparison.OrdinalIgnoreCase))
            {
                found = pid;
                return false; // stop enumeration — first match in z-order wins
            }

            return true;
        }, nint.Zero);

        return found;
    }

    private static List<nint> EnumerateVisibleTopLevelWindows(uint? pid)
    {
        var handles = new List<nint>();
        User32.EnumWindows((hWnd, _) =>
        {
            if (!User32.IsWindowVisible(hWnd))
                return true;
            if (pid.HasValue && GetPid(hWnd) != pid.Value)
                return true;

            handles.Add(hWnd);
            return true;
        }, nint.Zero);
        return handles;
    }

    // --- Collection (UIA) --------------------------------------------------------------------------

    private readonly record struct CollectResult(
        List<ObservedWindow> Windows, List<ObservedNode> Nodes, bool BudgetExceeded, long WalkMs, long HitMs);

    private CollectResult Collect(IReadOnlyList<nint> handles, bool hitTest, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var windows = new List<ObservedWindow>();
        var nodes = new List<ObservedNode>();
        var byRuntimeId = new Dictionary<string, int>(StringComparer.Ordinal);
        var budgetExceeded = false;
        var foreground = User32.GetForegroundWindow();
        var unreadablePids = new HashSet<uint>();

        using (BuildCacheRequest().Activate())
        {
            foreach (var handle in handles)
            {
                ct.ThrowIfCancellationRequested();
                if (stopwatch.Elapsed >= Budget)
                {
                    budgetExceeded = true;
                    break;
                }

                var pid = GetPid(handle);
                if (ReadWindow(handle, pid, unreadablePids, TryGetRoot, User32.IsWindowVisible, out var root) == WindowRead.Gone)
                    continue;

                // A window that did not answer is described by Win32 alone (title, rectangle): those
                // calls do not depend on the application's UI thread.
                var rect = root is null ? GetWindowRectangle(handle) : root.Properties.BoundingRectangle.ValueOrDefault;
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;

                var windowIndex = windows.Count;
                windows.Add(new ObservedWindow(
                    handle,
                    root is null ? GetWindowTitle(handle) : root.Properties.Name.ValueOrDefault ?? "",
                    GetClassName(handle),
                    GetProcessName(pid),
                    (int)pid,
                    handle == foreground,
                    IsModal(handle),
                    rect)
                {
                    Unreadable = root is null,
                });

                if (root is not null)
                    WalkNode(root, null, 0, windowIndex, nodes, byRuntimeId, ct);
            }
        }

        var walkMs = stopwatch.ElapsedMilliseconds;
        var hitMs = 0L;

        if (hitTest && !budgetExceeded)
        {
            budgetExceeded = HitTest(windows, nodes, byRuntimeId, stopwatch, ct);
            hitMs = stopwatch.ElapsedMilliseconds - walkMs;
        }

        return new CollectResult(windows, nodes, budgetExceeded, walkMs, hitMs);
    }

    private CacheRequest BuildCacheRequest()
    {
        var lib = _automation.PropertyLibrary;
        var cacheRequest = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            AutomationElementMode = AutomationElementMode.None,
        };
        cacheRequest.TreeFilter = new PropertyCondition(lib.Element.IsControlElement, true);

        foreach (var property in new[]
                 {
                     lib.Element.ControlType, lib.Element.Name, lib.Element.AutomationId, lib.Element.IsEnabled,
                     lib.Element.HasKeyboardFocus, lib.Element.IsOffscreen, lib.Element.BoundingRectangle,
                     lib.Element.IsPassword,
                     lib.Element.RuntimeId, _automation.PatternLibrary.InvokePattern.AvailabilityProperty!,
                     lib.Value.Value, lib.Toggle.ToggleState, lib.SelectionItem.IsSelected,
                     lib.ExpandCollapse.ExpandCollapseState,
                 })
        {
            cacheRequest.Add(property);
        }

        return cacheRequest;
    }

    /// <summary>Walks one cached subtree, dropping (with its subtree) any non-root node that is
    /// offscreen or too small to be meaningfully interacted with.</summary>
    private void WalkNode(
        AutomationElement element, int? parent, int depth, int windowIndex,
        List<ObservedNode> nodes, Dictionary<string, int> byRuntimeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var properties = element.Properties;
        var rect = properties.BoundingRectangle.ValueOrDefault;
        if (depth > 0 && !IsCollectible(properties.IsOffscreen.ValueOrDefault, rect))
            return;

        var index = nodes.Count;
        var frameworkElement = element.FrameworkAutomationElement;
        var lib = _automation.PropertyLibrary;

        var isPassword = properties.IsPassword.ValueOrDefault;
        var value = CollectedValue(
            isPassword,
            frameworkElement.TryGetPropertyValue<string>(lib.Value.Value, out var rawValue) ? rawValue : null);
        var toggle = frameworkElement.TryGetPropertyValue<ToggleState>(lib.Toggle.ToggleState, out var toggleState)
            ? toggleState.ToString()
            : null;
        var selected = frameworkElement.TryGetPropertyValue<bool>(lib.SelectionItem.IsSelected, out var isSelected) && isSelected;
        var expand = frameworkElement.TryGetPropertyValue<ExpandCollapseState>(lib.ExpandCollapse.ExpandCollapseState, out var expandState)
            ? expandState.ToString()
            : null;

        nodes.Add(new ObservedNode(
            index, parent, depth, windowIndex,
            properties.ControlType.ValueOrDefault.ToString(),
            properties.Name.ValueOrDefault ?? "",
            properties.AutomationId.ValueOrDefault ?? "",
            rect,
            properties.IsEnabled.ValueOrDefault,
            properties.HasKeyboardFocus.ValueOrDefault,
            value, toggle, selected, expand,
            HitVisible: null)
        {
            Password = isPassword,
        });

        var runtimeId = properties.RuntimeId.ValueOrDefault;
        if (runtimeId is { Length: > 0 })
            byRuntimeId[string.Join('.', runtimeId)] = index;

        foreach (var child in element.CachedChildren ?? [])
            WalkNode(child, index, depth + 1, windowIndex, nodes, byRuntimeId, ct);
    }

    /// <summary>Hit-tests only the nodes of the bottom-most (main) window of each process — windows
    /// stacked above it (popups, menus, dialogs) are trusted without hit-testing. Runs after the
    /// collecting <see cref="CacheRequest"/>'s scope has closed, so <c>_automation.FromPoint</c> below
    /// makes a fresh, non-cached query — matching the working reference (mcsprobe). Returns whether the
    /// time budget was exceeded partway through.</summary>
    private bool HitTest(
        List<ObservedWindow> windows, List<ObservedNode> nodes, Dictionary<string, int> byRuntimeId,
        Stopwatch stopwatch, CancellationToken ct)
    {
        var lastWindowIndexByPid = new Dictionary<int, int>();
        for (var i = 0; i < windows.Count; i++)
            lastWindowIndexByPid[windows[i].Pid] = i;

        for (var i = 0; i < nodes.Count; i++)
        {
            if (stopwatch.Elapsed >= Budget)
                return true;

            ct.ThrowIfCancellationRequested();

            var node = nodes[i];
            if (node.Parent is null || !HitTestableTypes.Contains(node.ControlType))
                continue;
            if (lastWindowIndexByPid[windows[node.Window].Pid] != node.Window)
                continue; // window stacked above the main window of its process — trusted as visible

            nodes[i] = node with { HitVisible = IsHitVisible(node, i, nodes, byRuntimeId) };
        }

        return false;
    }

    /// <summary>An element is visible at its own centre point when the live hit-test there resolves to
    /// the node itself, one of its descendants (an inner element intercepted the point), or one of its
    /// ancestors (the node itself isn't independently hit-testable, e.g. list/tree items).</summary>
    private bool IsHitVisible(ObservedNode node, int nodeIndex, List<ObservedNode> nodes, Dictionary<string, int> byRuntimeId)
    {
        AutomationElement hit;
        try
        {
            var cx = node.Rect.X + node.Rect.Width / 2;
            var cy = node.Rect.Y + node.Rect.Height / 2;
            hit = _automation.FromPoint(new Point(cx, cy));
        }
        catch (Exception)
        {
            return false;
        }

        string runtimeIdKey;
        try
        {
            runtimeIdKey = string.Join('.', hit.Properties.RuntimeId.ValueOrDefault ?? []);
        }
        catch (Exception)
        {
            return false;
        }

        if (!byRuntimeId.TryGetValue(runtimeIdKey, out var hitIndex))
            return false;

        for (int? k = hitIndex; k is not null; k = nodes[k.Value].Parent)
        {
            if (k == nodeIndex)
                return true;
        }

        for (int? k = node.Parent; k is not null; k = nodes[k.Value].Parent)
        {
            if (k == hitIndex)
                return true;
        }

        return false;
    }

    // --- Live resolution (FindLive) ------------------------------------------------------------------

    /// <summary>A window that dies or stops answering while it is searched simply has no match —
    /// resolution then ends in "element no longer present", never in a raw UIA/COM error.</summary>
    private T? TryInWindow<T>(nint hwnd, Func<T?> search) where T : class
    {
        try
        {
            return search();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Live resolution failed in window {Handle}", hwnd);
            return null;
        }
    }

    private AutomationElement? WalkPath(AutomationElement current, IReadOnlyList<LocatorStep> path, int stepIndex)
    {
        if (stepIndex == path.Count)
            return current;

        var step = path[stepIndex];
        var matchIndex = 0;
        foreach (var child in current.FindAllChildren(ControlViewCondition()))
        {
            // A sibling the collector would have dropped (offscreen/too small) was never counted by
            // ObservationBuilder when it assigned this step's Index — it must not be counted here either.
            if (!IsLiveCollectible(child))
                continue;
            if (!MatchesStep(child, step))
                continue;

            if (matchIndex == step.Index)
                return WalkPath(child, path, stepIndex + 1);

            matchIndex++;
        }

        return null;
    }

    private List<AutomationElement> FindDescendantsByTypeAndKey(AutomationElement root, LocatorStep step)
    {
        var results = new List<AutomationElement>();
        if (!Enum.TryParse<ControlType>(step.ControlType, out var controlType))
            return results;

        var condition = new AndCondition(ControlViewCondition(), _automation.ConditionFactory.ByControlType(controlType));
        foreach (var descendant in root.FindAllDescendants(condition))
        {
            // Same eligibility rule as the collector — a hidden/tiny twin the collector would have
            // dropped must not count towards "exactly one candidate" either.
            if (IsLiveCollectible(descendant) && MatchesStep(descendant, step))
                results.Add(descendant);
        }

        return results;
    }

    /// <summary>Live counterpart of <see cref="IsCollectible"/>: reads IsOffscreen/BoundingRectangle with
    /// the collector's <c>ValueOrDefault</c> semantics (an unsupported property counts the same way it
    /// did during collection, so sibling indexes stay aligned). The catch is only a last-resort guard
    /// for genuinely dead elements.</summary>
    private static bool IsLiveCollectible(AutomationElement element)
    {
        try
        {
            var properties = element.Properties;
            return IsCollectible(properties.IsOffscreen.ValueOrDefault, properties.BoundingRectangle.ValueOrDefault);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Matches the same (ControlType, key) pair <see cref="ObservationBuilder"/> uses when it
    /// builds locator paths, reading properties exactly like the collector (<c>ValueOrDefault</c>) —
    /// plain accessors throw for unsupported properties such as a missing AutomationId.</summary>
    private static bool MatchesStep(AutomationElement element, LocatorStep step)
    {
        try
        {
            var properties = element.Properties;
            var controlType = properties.ControlType.ValueOrDefault.ToString();
            var key = ObservationBuilder.LocatorKey(properties.AutomationId.ValueOrDefault, properties.Name.ValueOrDefault);
            return controlType == step.ControlType && key == step.Key;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The same "control view" filter the collecting <see cref="CacheRequest"/> applies
    /// (<c>IsControlElement = true</c>) — required so live sibling indices line up with the ones
    /// <see cref="ObservationBuilder"/> assigned when it built the locator.</summary>
    private PropertyCondition ControlViewCondition() =>
        new(_automation.PropertyLibrary.Element.IsControlElement, true);

    // --- Small Win32 helpers --------------------------------------------------------------------------

    /// <summary>Null when the window's UIA root (inside a collecting <see cref="CacheRequest"/>: its
    /// whole subtree) cannot be read — the window is gone, or its application does not answer. Logged
    /// at Warning: an application that stops answering is what an operator has to find in the log.</summary>
    private AutomationElement? TryGetRoot(nint handle)
    {
        try
        {
            return _automation.FromHandle(handle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("UIA root of window {Handle} could not be read: {ExceptionType}: {Message}",
                handle, ex.GetType().Name, ex.Message);
            return null;
        }
    }

    private static uint GetPid(nint handle)
    {
        User32.GetWindowThreadProcessId(handle, out var pid);
        return pid;
    }

    private static string GetWindowTitle(nint handle)
    {
        var buffer = new char[256];
        var length = User32.GetWindowTextW(handle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static Rectangle GetWindowRectangle(nint handle) =>
        User32.GetWindowRect(handle, out var rect)
            ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : Rectangle.Empty;

    private static string GetClassName(nint handle)
    {
        var buffer = new char[256];
        var length = User32.GetClassNameW(handle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "";
    }

    private static bool IsModal(nint handle)
    {
        var owner = User32.GetWindow(handle, User32.GW_OWNER);
        return owner != nint.Zero && !User32.IsWindowEnabled(owner);
    }

    private static string GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    private static string StripExeSuffix(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
