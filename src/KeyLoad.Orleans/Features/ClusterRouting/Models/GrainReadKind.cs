namespace KeyLoad.Orleans;

/// <summary>Version-one database read capabilities routed through independent read actors.</summary>
public enum GrainReadKind
{
    /// <summary>Resolve an API key to the current persisted principal.</summary>
    Authenticate,
    /// <summary>Read a document under row and field authorization.</summary>
    Document,
    /// <summary>Read a bounded stream page.</summary>
    Stream,
    /// <summary>Read a bounded partition event source page.</summary>
    EventSource,
    /// <summary>Read durable subscription status.</summary>
    Subscription,
    /// <summary>Inspect a queue message with authorized payload visibility.</summary>
    Message,
    /// <summary>Traverse a graph under shared read-work limits.</summary>
    Traverse,
    /// <summary>Read a bounded time-series page.</summary>
    Samples,
    /// <summary>Execute an authorized text query.</summary>
    Query,
    /// <summary>Execute an authorized typed query AST.</summary>
    AstQuery,
    /// <summary>Return supported query capabilities to an authenticated principal.</summary>
    QueryCapabilities,
    /// <summary>Read a bounded durable change-feed page.</summary>
    ChangeFeed,
    /// <summary>Establish a signed live-query cursor.</summary>
    LiveQueryStart,
    /// <summary>Advance a signed live-query cursor.</summary>
    LiveQueryRead,
    /// <summary>Read authorized partition outbox status.</summary>
    OutboxStatus,
    /// <summary>Read an authorized projection batch.</summary>
    ProjectionBatch,
    /// <summary>Execute authorized lexical, vector or hybrid search.</summary>
    Search,
    /// <summary>Create an administrator-authorized node backup.</summary>
    Backup,
    /// <summary>Read administrator-authorized node admission diagnostics.</summary>
    Admission,
    /// <summary>Read administrator-authorized physical node status.</summary>
    NodeStatus,
    /// <summary>Read authorized current blob metadata or a tombstone.</summary>
    BlobMetadata,
    /// <summary>Read creator-authorized blob upload progress.</summary>
    BlobUploadInfo,
    /// <summary>Read a bounded exact blob byte range at its current revision.</summary>
    BlobRange,
    /// <summary>List bounded authorized current blob metadata.</summary>
    BlobList,
    /// <summary>Read administrator-authorized physical node observations.</summary>
    AdminDashboard,
    /// <summary>Read a bounded administrator-authorized resource metadata page.</summary>
    AdminResources,
    /// <summary>Read queue counters and metadata without consuming messages.</summary>
    AdminQueue,
    /// <summary>Read the projected latest sample at an optional inclusive timestamp.</summary>
    LatestSample,
    /// <summary>Read complete bounded raw statistics over a half-open sample range.</summary>
    AggregateSamples,
    /// <summary>Read dense bounded fixed-width UTC sample windows.</summary>
    AggregateSampleWindows,
    /// <summary>Read the persisted exclusive time-series floor and purge progress.</summary>
    SampleRetention,
    /// <summary>Read one compatible snapshot and complete bounded event tail.</summary>
    AggregateReplay,
    /// <summary>Read one authorized durable source transfer and its signed intent.</summary>
    QueueTransfer,
    /// <summary>Read an authorized destination receipt without claiming source completion.</summary>
    QueueTransferReceipt,
    /// <summary>Execute distinct graph scope, retrieval and expansion under one read cut.</summary>
    GraphSearch,
    /// <summary>Inspect a recurring schedule through current persisted projection policies.</summary>
    RecurringSchedule,
    /// <summary>Inspect current projected saga state without exposing its retained timeout template.</summary>
    Saga,
    /// <summary>Compile versioned SQL graph operators to the same authorized search executor.</summary>
    SqlGraphSearch,
    /// <summary>Read one authorized shortest graph path inside a committed partition cut.</summary>
    GraphShortestPath,
    /// <summary>Execute bounded SQL graph-path syntax through the canonical path reader.</summary>
    SqlGraphPath,
    /// <summary>Read a persisted administrator-authorized atomic-partition placement witness.</summary>
    AtomicPartitionPlacement
}
