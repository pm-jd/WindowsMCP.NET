using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

/// <summary>
/// Keeps element id → <see cref="ElementLocator"/> for the last <see cref="MaxObservations"/>
/// observations (LRU by observation, not by id): each <see cref="Remember"/> call adds one
/// observation's ids; once more than <see cref="MaxObservations"/> have been remembered, the
/// oldest observation's ids are evicted — unless the same id also appears in one of the observations
/// still retained, in which case it keeps resolving. Ids from an evicted observation fail with
/// <see cref="ElementNotFoundException"/> so callers know to call Observe again.
/// </summary>
public sealed class ObservationStore
{
    private const int MaxObservations = 8;

    private readonly Lock _lock = new();
    private readonly Queue<Dictionary<string, ElementLocator>> _history = new();
    private Dictionary<string, ElementLocator> _lookup = new(StringComparer.Ordinal);

    public void Remember(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var ids = new Dictionary<string, ElementLocator>(StringComparer.Ordinal);
        foreach (var element in observation.Elements)
            ids[element.Id] = element.Locator;

        lock (_lock)
        {
            _history.Enqueue(ids);
            while (_history.Count > MaxObservations)
                _history.Dequeue();

            var merged = new Dictionary<string, ElementLocator>(StringComparer.Ordinal);
            foreach (var observationIds in _history)
            {
                foreach (var (id, locator) in observationIds)
                    merged[id] = locator;
            }

            _lookup = merged;
        }
    }

    public ElementLocator Get(string id)
    {
        lock (_lock)
        {
            if (_lookup.TryGetValue(id, out var locator))
                return locator;
        }

        throw new ElementNotFoundException(id);
    }
}
