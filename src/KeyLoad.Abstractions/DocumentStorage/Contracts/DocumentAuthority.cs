namespace KeyLoad;

/// <summary>Selects which source owns a resource’s canonical document state.</summary>
public enum DocumentAuthority
{
    /// <summary>Documents are the canonical state.</summary>
    Document,
    /// <summary>The event stream is the canonical state.</summary>
    EventStream
}
