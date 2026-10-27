namespace KeyLoad.Clustering;

/// <summary>Closed diagnostic shapes; never contains exception type names or caller text.</summary>
internal enum GrainFailureCauseKind
{
    /// <summary>No selected private cause classification applies.</summary>
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
    AggregateDepthLimit,
    /// <summary>A partition-query lifetime request failed its original source identity checks.</summary>
    PartitionQueryLifetimeRequest,
    /// <summary>A partition-query receiving physical owner differs from its original owner.</summary>
    PartitionQueryReceivingOwner,
    /// <summary>A partition-query placement witness differs from its expected physical owner.</summary>
    PartitionQueryPlacementOwner,
    /// <summary>Partition-query leaves disagree on their physical owner or policy epoch.</summary>
    PartitionQueryLeafOwner,
    /// <summary>The original partition-query leaf set is incomplete.</summary>
    PartitionQueryIncomplete,
    /// <summary>A partition-query source principal or resource definition is invalid.</summary>
    PartitionQuerySourceMetadata,
    /// <summary>A partition-query server physical owner identity is invalid.</summary>
    PartitionQueryPhysicalOwner,
    /// <summary>An operation targets different physical storage files.</summary>
    PhysicalStorageOwner,
    /// <summary>An entity reference differs from the authenticated resource lifetime.</summary>
    EntityResourceLifetime,
    /// <summary>An asynchronous operation exceeded its deadline.</summary>
    Deadline
}
