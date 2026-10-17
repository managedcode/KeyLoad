namespace KeyLoad;

/// <summary>Identifies the kind of event source.</summary>
public enum EventSourceKind
{
    /// <summary>A named business topic.</summary>
    Topic,
    /// <summary>An entity event stream.</summary>
    Stream
}
