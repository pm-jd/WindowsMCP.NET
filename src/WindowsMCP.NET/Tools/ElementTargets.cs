using WindowsMcpNet.Models;
using WindowsMcpNet.Services;

namespace WindowsMcpNet.Tools;

/// <summary>An element id resolved against the live UI: the action target, a human-readable
/// description for result strings, and the locator it came from (for process-scoped verification).</summary>
internal sealed record ResolvedElement(IActionTarget Target, string Describe, ElementLocator Locator);

/// <summary>Shared id → live element resolution for the element-id paths of Click/Type/MultiSelect/MultiEdit.</summary>
internal static class ElementTargets
{
    public static ResolvedElement Resolve(string id, ObservationStore store, ObservationService svc)
    {
        var locator = store.Get(id);
        var element = svc.FindLive(locator) ?? throw new ElementNotFoundException(id);

        string describe;
        try
        {
            describe = $"{id} ({element.ControlType} '{element.Name}')";
        }
        catch (Exception)
        {
            describe = id;
        }

        return new ResolvedElement(new FlaUiActionTarget(element), describe, locator);
    }

    /// <summary>Verification signature scoped to the element's process. Returns <c>"-"</c> when the
    /// process has no visible window any more: the action has already happened and must not fail now.</summary>
    public static Func<string> SignatureFor(ResolvedElement resolved, ObservationService svc, CancellationToken ct) =>
        () =>
        {
            try
            {
                return svc.Signature(ObserveScope.Process, resolved.Locator.Process, ct);
            }
            catch (InvalidOperationException)
            {
                return "-";
            }
        };
}
