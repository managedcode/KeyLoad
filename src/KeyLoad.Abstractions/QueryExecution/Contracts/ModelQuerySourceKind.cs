namespace KeyLoad.Query;

/// <summary>Identifies an authorized read-only database model source.</summary>
public enum ModelQuerySourceKind
{
    /// <summary>Reads retained records of one event stream generation.</summary>
    Events,
    /// <summary>Inspects retained metadata and bodies of one queue lane.</summary>
    QueueMessages,
    /// <summary>Reads retained records of one topic generation.</summary>
    TopicEvents
}
