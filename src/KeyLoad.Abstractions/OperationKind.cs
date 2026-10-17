namespace KeyLoad;

/// <summary>Identifies a trusted operation recorded in the replicated command envelope.</summary>
public enum OperationKind
{
    /// <summary>The batch value.</summary>
    Batch,
    /// <summary>The receive value.</summary>
    Receive,
    /// <summary>The delivery value.</summary>
    Delivery,
    /// <summary>The processing value.</summary>
    Processing,
    /// <summary>The configure resource value.</summary>
    ConfigureResource,
    /// <summary>The configure principal value.</summary>
    ConfigurePrincipal,
    /// <summary>The configure api key value.</summary>
    ConfigureApiKey,
    /// <summary>The set dispatch value.</summary>
    SetDispatch,
    // Discriminator 8 is permanently reserved and must never be reused.
    /// <summary>The configure subscription value.</summary>
    ConfigureSubscription = 9,
    /// <summary>The seek subscription value.</summary>
    SeekSubscription,
    /// <summary>The receive subscription value.</summary>
    ReceiveSubscription,
    /// <summary>The subscription delivery value.</summary>
    SubscriptionDelivery,
    /// <summary>The subscription processing value.</summary>
    SubscriptionProcessing,
    /// <summary>The set subscription paused value.</summary>
    SetSubscriptionPaused,
    /// <summary>The configure projection consumer value.</summary>
    ConfigureProjectionConsumer,
    /// <summary>The commit projection batch value.</summary>
    CommitProjectionBatch,
    /// <summary>The release projection consumer value.</summary>
    ReleaseProjectionConsumer,
    /// <summary>The purge outbox value.</summary>
    PurgeOutbox,
    /// <summary>Begins a reserved binary upload lifetime.</summary>
    BeginBlobUpload,
    /// <summary>Accepts one bounded binary part.</summary>
    WriteBlobPart,
    /// <summary>Publishes an accepted binary upload through revision CAS.</summary>
    CompleteBlobUpload,
    /// <summary>Aborts a binary upload and releases unused reservation.</summary>
    AbortBlobUpload,
    /// <summary>Publishes a binary object revision tombstone.</summary>
    DeleteBlob,
    /// <summary>Reclaims eligible binary parts through a bounded command.</summary>
    ReclaimBlob,
    /// <summary>Commits the initial physical shard placement catalog.</summary>
    BootstrapPhysicalShardCatalog,
    /// <summary>Commits an explicit atomic-partition assignment to its physical shard.</summary>
    BindAtomicPartitionPlacement,
    /// <summary>Changes private native runtime journal infrastructure through RF3.</summary>
    RuntimeJournal,
    /// <summary>Composes independent queue receives through request grains; never replicated as one command.</summary>
    ReceiveAcrossLanes,
    /// <summary>Commits server-confirmed physical owner registration at the stable control authority.</summary>
    RegisterPhysicalOwner,
    /// <summary>Runs administrator-only native ANN generation maintenance through independently authorized child requests.</summary>
    MaintainAnnIndex,
    /// <summary>Runs protected native text generation maintenance through independently authorized child requests.</summary>
    MaintainTextIndex,
    /// <summary>Applies one receiver-verified private physical movement phase through RF3.</summary>
    PartitionMovementPhase,
    /// <summary>Coordinates protected partition movement through separately authorized child requests.</summary>
    MovePartition,
    /// <summary>Maintains an administrator-authorized online text generation through separate request grains.</summary>
    MaintainOnlineTextIndex = 34,
    /// <summary>Commits one freshly verified private online text publication; excluded from public operation factories.</summary>
    OnlineTextPublicationPhase = 35,
    /// <summary>Reserves verified event-feed control admission; unavailable until its owning implementation is joined.</summary>
    EventFeedControl = 36,
    /// <summary>Reserves private verified event-feed source phases; excluded from generic public operation factories.</summary>
    EventFeedSourcePhase = 37,
    /// <summary>Commits target-only inbox identity and effects without source acknowledgement.</summary>
    CommitInbox = 38,
    /// <summary>Commits a freshly verified remote graph source effect through its physical owner.</summary>
    RemoteGraphSource = 39,
    /// <summary>Commits a freshly verified remote graph receiving effect through its physical owner.</summary>
    RemoteGraphReceive = 40,
    /// <summary>Commits a freshly verified remote graph receipt completion through its source owner.</summary>
    RemoteGraphComplete = 41,
    /// <summary>Coordinates an explicit remote graph edge upsert through separate owner requests.</summary>
    UpsertRemoteGraphEdge = 42,
    /// <summary>Coordinates an explicit remote graph edge deletion through separate owner requests.</summary>
    DeleteRemoteGraphEdge = 43,
    /// <summary>Coordinates an explicit remote graph delivery through separate owner receipts.</summary>
    DeliverRemoteGraphEdge = 44,
    /// <summary>Commits a freshly verified private BM25 corpus publication; excluded from generic public factories.</summary>
    Bm25CorpusPublicationPhase = 45,
    /// <summary>Persists an authorized capacity placement policy at Control RF3.</summary>
    ConfigurePlacementPolicy = 46,
    /// <summary>Persists one original signed complete placement plan.</summary>
    PersistPlacementIntent = 47,
    /// <summary>Applies an intent through its immutable original native Move requests.</summary>
    ApplyPlacementIntent = 48,
    /// <summary>Commits one private freshly authenticated placement phase.</summary>
    PlacementIntentPhase = 49,
    /// <summary>Changes one existing queue pause state through an explicit schema-version CAS.</summary>
    SetQueueDispatch = 50,
    /// <summary>Atomically creates one absent canonical typed table with an original caller identity.</summary>
    CreateTable = 51,
    /// <summary>Commits one authenticated internal exact-lifetime retirement phase; excluded from public factories.</summary>
    ResourceRetirementPhase = 52,
    /// <summary>Native all-owner table retirement and logical removal through the authenticated request grain.</summary>
    DropTable = 53
}
