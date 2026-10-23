namespace KeyLoad.Clustering;

/// <summary>Database read capabilities routed through independent read actors.</summary>
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
    /// <summary>Read administrator-authorized physical node observations.</summary>
    AdminDashboard = 24,
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
    /// <summary>Read one complete bounded query over same-owner atomic partitions.</summary>
    PartitionQuery = 41,
    /// <summary>Read the bounded eventual reverse-adjacency projection for one visible graph target.</summary>
    GraphIncomingEdges,
    /// <summary>Reads a private execution runtime journal header.</summary>
    RuntimeJournalHeader,
    /// <summary>Reads one pinned private execution runtime journal page.</summary>
    RuntimeJournalPage,
    /// <summary>Reads the complete admitted private execution runtime journal catalog.</summary>
    RuntimeJournalCatalog,
    /// <summary>Reads one current revisioned derived time-series rollup.</summary>
    SampleRollup,
    /// <summary>Publishes the eligible execution lexical generation at an acknowledged applied cut.</summary>
    WaitForIndex,
    /// <summary>Executes one issued internal administrator ANN maintenance phase on the exact execution owner.</summary>
    AnnMaintenance,
    /// <summary>Executes one issued protected execution text maintenance stage on its exact owner.</summary>
    TextMaintenance,
    /// <summary>Reads one provisioned vector-only execution ANN generation under current policy.</summary>
    ApproximateSearch = 52,
    /// <summary>Requires an actual provisioned execution ANN generation to cover an acknowledged minimum prefix.</summary>
    WaitForAnnIndex = 63,
    /// <summary>Read one authorized bounded sample-chunk window at its committed cut.</summary>
    SampleChunkWindow = 65,
    /// <summary>Executes one privately authenticated online text-maintenance phase at its exact execution owner.</summary>
    OnlineTextMaintenance,
    /// <summary>Read complete globally ranked canonical text/vector results over explicit authorized physical owners.</summary>
    DistributedSearch,
    /// <summary>Privately observes source attempt state or an authenticated actual target capacity failure.</summary>
    QueueTransferCoordination = 72,
    /// <summary>Privately executes a partition query leaf under mandatory exact metadata and principal proof.</summary>
    PartitionQueryLeaf = 88,
    /// <summary>Privately executes one distributed search phase under mandatory exact metadata and principal proof.</summary>
    DistributedSearchLeaf = 89,
    /// <summary>Privately observes execution ANN publication and data under mandatory lifetime coverage.</summary>
    AnnProjectionObservation = 90,
    /// <summary>Reads bounded canonical user analytics events under current persisted authority.</summary>
    AnalyticsEvents = 94,
    /// <summary>Aggregates a bounded authorized user analytics source cut through the execution derived engine.</summary>
    AnalyticsAggregate = 95,
    /// <summary>Reads authorized file metadata without physical payload bytes.</summary>
    FileMetadata = 97
}
