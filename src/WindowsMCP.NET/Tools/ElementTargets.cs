using WindowsMcpNet.Models;
using WindowsMcpNet.Native;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

/// <summary>An element id resolved against the live UI: the action target, a human-readable
/// description for result strings, the locator it came from, the top-level window it was found in and
/// that window's process id (for instance-scoped verification).</summary>
internal sealed record ResolvedElement(IActionTarget Target, string Describe, ElementLocator Locator, int Pid, nint WindowHandle);

/// <summary>Shared id to live element resolution for the element-id paths of Click/Type/MultiSelect/MultiEdit.</summary>
internal static class ElementTargets
{
    /// <summary>
    /// Resolves <paramref name="id"/> to a live target. Everything here happens before any action, so
    /// every failure means "nothing was done": an unknown, stale or vanished element is an
    /// <see cref="ElementNotFoundException"/>; an element whose window is disabled by a modal dialog is
    /// refused (see <see cref="EnsureWindowNotBlocked"/>).
    /// </summary>
    public static ResolvedElement Resolve(string id, ObservationStore store, ObservationService svc)
    {
        var stored = store.Get(id);
        var (element, hwnd) = svc.FindLive(stored) ?? throw new ElementNotFoundException(id);
        EnsureWindowNotBlocked(id, User32.IsWindowEnabled(hwnd));

        User32.GetWindowThreadProcessId(hwnd, out var windowPid);

        string controlType, name;
        int elementPid;
        try
        {
            var properties = element.Properties;
            controlType = properties.ControlType.ValueOrDefault.ToString();
            name = properties.Name.ValueOrDefault ?? "";
            // Usually the window's process; differs for content hosted from another process, where the
            // element's own process is what a hit-test at its position reports.
            elementPid = properties.ProcessId.TryGetValue(out var processId) ? processId : (int)windowPid;
        }
        catch (Exception)
        {
            throw new ElementNotFoundException(id);
        }

        return new ResolvedElement(
            new FlaUiActionTarget(element, id, controlType, elementPid),
            $"{id} ({controlType} '{name}')", stored.Locator, (int)windowPid, hwnd);
    }

    /// <summary>
    /// A window that owns an open modal dialog is disabled: the user cannot reach its controls, but UIA
    /// patterns (Invoke, SetValue) would still act on them behind the dialog. The element path refuses
    /// that — the agent has to deal with the dialog first.
    /// </summary>
    internal static void EnsureWindowNotBlocked(string id, bool windowEnabled)
    {
        if (!windowEnabled)
            throw new InvalidOperationException(
                $"element {id} is in a window blocked by a modal dialog — call Observe and handle the dialog first");
    }

    /// <summary>Verification signature scoped to the process instance (pid) that owns the window the
    /// element was resolved in.</summary>
    public static Func<string> SignatureFor(ResolvedElement resolved, ObservationService svc, CancellationToken ct) =>
        GuardSignature(() => svc.Signature(resolved.Pid, ct));

    /// <summary>Maps any failure of the after-walk (window gone, UIA error) to the constant <c>"-"</c>:
    /// the action has already happened and must not turn into an error. Cancellation still propagates.</summary>
    internal static Func<string> GuardSignature(Func<string> read) =>
        () =>
        {
            try
            {
                return read();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return "-";
            }
        };

    internal static int ClampSettle(int settleMs) => Math.Clamp(settleMs, 0, 2000);
}
