namespace KeyLoad;

/// <summary>Identifies an operation capability that can be granted to a principal.</summary>
[Flags]
public enum Capability : long
{
    /// <summary>Grants no capabilities.</summary>
    None = 0,
    /// <summary>Allows reading documents.</summary>
    DocumentsRead = 1L << 0,
    /// <summary>Allows creating and changing documents.</summary>
    DocumentsWrite = 1L << 1,
    /// <summary>Allows query execution.</summary>
    Query = 1L << 2,
    /// <summary>Allows appending events.</summary>
    EventsAppend = 1L << 3,
    /// <summary>Allows reading events.</summary>
    EventsRead = 1L << 4,
    /// <summary>Allows publishing queue messages.</summary>
    QueuePublish = 1L << 5,
    /// <summary>Allows consuming queue messages.</summary>
    QueueConsume = 1L << 6,
    /// <summary>Allows acknowledging queue deliveries.</summary>
    QueueAck = 1L << 7,
    /// <summary>Allows renewing queue leases.</summary>
    QueueRenew = 1L << 8,
    /// <summary>Allows inspecting queue state.</summary>
    QueueInspect = 1L << 9,
    /// <summary>Allows reading dead-lettered messages.</summary>
    DeadLettersRead = 1L << 10,
    /// <summary>Allows redriving dead-lettered messages.</summary>
    DeadLettersRedrive = 1L << 11,
    /// <summary>Allows managing subscriptions.</summary>
    SubscriptionsManage = 1L << 12,
    /// <summary>Allows reading graph edges.</summary>
    GraphRead = 1L << 13,
    /// <summary>Allows changing graph edges.</summary>
    GraphWrite = 1L << 14,
    /// <summary>Allows reading time-series samples.</summary>
    SeriesRead = 1L << 15,
    /// <summary>Allows appending time-series samples.</summary>
    SeriesAppend = 1L << 16,
    /// <summary>Allows searching vector indexes.</summary>
    VectorSearch = 1L << 17,
    /// <summary>Allows raw vector access.</summary>
    VectorRaw = 1L << 18,
    /// <summary>Allows managing resource schemas.</summary>
    SchemaManage = 1L << 19,
    /// <summary>Allows managing principals and credentials.</summary>
    SecurityManage = 1L << 20,
    /// <summary>Allows managing backups.</summary>
    BackupManage = 1L << 21,
    /// <summary>Allows restoring backups.</summary>
    BackupRestore = 1L << 22,
    /// <summary>Allows diagnostic operations.</summary>
    Diagnose = 1L << 23,
    /// <summary>Allows exporting data.</summary>
    DataExport = 1L << 24,
    /// <summary>Allows publishing topic events.</summary>
    TopicsPublish = 1L << 25,
    /// <summary>Allows reading topic events.</summary>
    TopicsRead = 1L << 26,
    /// <summary>Allows consuming subscription deliveries.</summary>
    SubscriptionsConsume = 1L << 27,
    /// <summary>Allows acknowledging subscription deliveries.</summary>
    SubscriptionsAck = 1L << 28,
    /// <summary>Allows reading change feeds.</summary>
    ChangesRead = 1L << 29,
    /// <summary>Allows reading binary object metadata and ranges.</summary>
    BlobRead = 1L << 30,
    /// <summary>Allows scoped binary upload lifecycle and progress.</summary>
    BlobWrite = 1L << 31,
    /// <summary>Allows conditional binary object deletion.</summary>
    BlobDelete = 1L << 32,
    /// <summary>Allows bounded cleanup of eligible binary versions.</summary>
    BlobManage = 1L << 33,
    /// <summary>Allows advancing bounded time-series retention.</summary>
    SeriesManage = 1L << 34,
    /// <summary>Allows complete raw worker replay of event streams and derived state.</summary>
    EventsReplay = 1L << 35,
    /// <summary>Allows conditional replacement of bounded aggregate snapshots.</summary>
    EventsSnapshotsManage = 1L << 36,
    /// <summary>Allows managing and evaluating durable recurring schedules and saga timeouts.</summary>
    SchedulerManage = 1L << 37,
    /// <summary>Allows conditionally cancelling queue messages.</summary>
    QueueCancel = 1L << 38,
    /// <summary>Allows target-only protected processing in a configured inbox namespace.</summary>
    InboxWrite = 1L << 39,
    /// <summary>Allows reading canonical user analytics events and aggregates.</summary>
    AnalyticsRead = 1L << 40,
    /// <summary>Allows conditional canonical user analytics event mutation.</summary>
    AnalyticsWrite = 1L << 41,
    /// <summary>Combines every defined capability.</summary>
    All = (1L << 42) - 1
}
