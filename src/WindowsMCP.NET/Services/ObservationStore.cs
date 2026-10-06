using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

/// <summary>
/// What the store remembers per element id: the locator, plus the top-level window it was observed in
/// and whether that window was transient (popup, menu, dialog). The window is what gives an id its
/// affinity when it is resolved — see <see cref="ObservationService.FindLive"/>.
/// </summary>
public sealed record StoredElement(ElementLocator Locator, nint WindowHandle, bool Transient);

/// <summary>
/// Keeps element id → <see cref="StoredElement"/> for the last <see cref="MaxObservations"/>
/// observations (LRU by observation, not by id): each <see cref="Remember"/> call adds one
/// observation's ids; once more than <see cref="MaxObservations"/> have been remembered, the
/// oldest observation's ids are evicted — unless the same id also appears in one of the observations
/// still retained, in which case it keeps resolving (with the window of the newest observation that
/// contains it). Ids from an evicted observation fail with <see cref="ElementNotFoundException"/> so
/// callers know to call Observe again.
/// </summary>
public sealed class ObservationStore
{
    private const int MaxObservations = 8;

    private readonly Lock _lock = new();
    private readonly Queue<Dictionary<string, StoredElement>> _history = new();
    private Dictionary<string, StoredElement> _lookup = new(StringComparer.Ordinal);

    public void Remember(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var ids = new Dictionary<string, StoredElement>(StringComparer.Ordinal);
        foreach (var element in observation.Elements)
            ids[element.Id] = new StoredElement(element.Locator, element.WindowHandle, element.Transient);

        lock (_lock)
        {
            _history.Enqueue(ids);
            while (_history.Count > MaxObservations)
                _history.Dequeue();

            var merged = new Dictionary<string, StoredElement>(StringComparer.Ordinal);
            foreach (var observationIds in _history)
            {
                foreach (var (id, stored) in observationIds)
                    merged[id] = stored;
            }

            _lookup = merged;
        }
    }

    /// <summary>The stored element for an id, or null when no retained observation contains it.</summary>
    public StoredElement? Find(string id)
    {
        lock (_lock)
            return _lookup.GetValueOrDefault(id);
    }

    public StoredElement Get(string id)
    {
        lock (_lock)
        {
            if (_lookup.TryGetValue(id, out var stored))
                return stored;
        }

        throw new ElementNotFoundException(id);
    }
}
