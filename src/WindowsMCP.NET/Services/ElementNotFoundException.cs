namespace WindowsMcpNet.Services;

/// <summary>Thrown when a stored element id no longer resolves to a live element (evicted, or the UI changed).</summary>
public sealed class ElementNotFoundException(string id) : Exception($"element {id} no longer present — call Observe");
