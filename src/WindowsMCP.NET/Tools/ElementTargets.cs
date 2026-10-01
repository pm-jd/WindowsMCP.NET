using WindowsMcpNet.Models;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

/// <summary>An element id resolved against the live UI: the action target, a human-readable
/// description for result strings, the locator it came from and the owning process id (for
/// instance-scoped verification).</summary>
internal sealed record ResolvedElement(IActionTarget Target, string Describe, ElementLocator Locator, int Pid);

/// <summary>Shared id to live element resolution for the element-id paths of Click/Type/MultiSelect/MultiEdit.</summary>
internal static class ElementTargets
{
    public static ResolvedElement Resolve(string id, ObservationStore store, ObservationService svc)
    {
        var locator = store.Get(id);
        var element = svc.FindLive(locator) ?? throw new ElementNotFoundException(id);

        int pid;
        try
        {
            pid = element.Properties.ProcessId.Value;
        }
        catch (Exception)
        {
            throw new ElementNotFoundException(id);
        }

        string describe;
        try
        {
            describe = $"{id} ({element.ControlType} '{element.Name}')";
        }
        catch (Exception)
        {
            describe = id;
        }

        return new ResolvedElement(new FlaUiActionTarget(element), describe, locator, pid);
    }

    /// <summary>Verification signature scoped to the element's own process instance (pid).</summary>
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
