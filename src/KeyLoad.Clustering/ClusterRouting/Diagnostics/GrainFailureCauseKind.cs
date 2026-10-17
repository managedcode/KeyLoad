namespace KeyLoad.Clustering;

/// <summary>Closed diagnostic shapes; never contains exception type names or caller text.</summary>
internal enum GrainFailureCauseKind
{
    /// <summary>The existing category does not require unexpected-cause inspection.</summary>
    Unobserved,
    /// <summary>An invalid operation state.</summary>
    State,
    /// <summary>A released or disposed operation state.</summary>
    ReleasedState,
    /// <summary>A missing object reference.</summary>
    Reference,
    /// <summary>A failed conversion, format or arithmetic representation.</summary>
    Conversion,
    /// <summary>A type initializer failed.</summary>
    Initialization,
    /// <summary>A required type or member could not be resolved.</summary>
    TypeResolution,
    /// <summary>An operation is unsupported.</summary>
    Unsupported,
    /// <summary>A serializer reported an invalid representation.</summary>
    Serialization,
    /// <summary>A storage or stream input/output operation failed.</summary>
    StorageIo,
    /// <summary>No selected closed shape matched.</summary>
    Other,
    /// <summary>The primary aggregate chain exceeded the bounded inspection depth.</summary>
    AggregateDepthLimit
}
