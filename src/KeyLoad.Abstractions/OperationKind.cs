namespace KeyLoad;

/// <summary>Identifies a trusted operation recorded in the replicated command envelope.</summary>
public enum OperationKind
{
    /// <summary>The batch value.</summary>
    Batch = 0,
    /// <summary>The receive value.</summary>
    Receive = 1,
    /// <summary>The delivery value.</summary>
    Delivery = 2,
    /// <summary>The processing value.</summary>
    Processing = 3,
    /// <summary>The configure resource value.</summary>
    ConfigureResource = 4,
    /// <summary>The configure principal value.</summary>
    ConfigurePrincipal = 5,
    /// <summary>The configure api key value.</summary>
    ConfigureApiKey = 6,
    /// <summary>The set dispatch value.</summary>
    SetDispatch = 7,
    // Discriminator 8 is permanently reserved and must never be reused.
    /// <summary>The configure subscription value.</summary>
    ConfigureSubscription = 9,
    /// <summary>The seek subscription value.</summary>
    SeekSubscription = 10,
    /// <summary>The receive subscription value.</summary>
    ReceiveSubscription = 11,
    /// <summary>The subscription delivery value.</summary>
    SubscriptionDelivery = 12,
    /// <summary>The subscription processing value.</summary>
    SubscriptionProcessing = 13,
    /// <summary>The set subscription paused value.</summary>
    SetSubscriptionPaused = 14,
    /// <summary>The configure projection consumer value.</summary>
    ConfigureProjectionConsumer = 15,
    /// <summary>The commit projection batch value.</summary>
    CommitProjectionBatch = 16,
    /// <summary>The release projection consumer value.</summary>
    ReleaseProjectionConsumer = 17,
    /// <summary>The purge outbox value.</summary>
    PurgeOutbox = 18,
    /// <summary>Changes private execution runtime journal infrastructure through replicated.</summary>
    RuntimeJournal = 27,
    /// <summary>Composes independent queue receives through request grains; never replicated as one command.</summary>
    ReceiveAcrossLanes = 28,
    /// <summary>Runs administrator-only execution ANN generation maintenance through independently authorized child requests.</summary>
    MaintainAnnIndex = 30,
    /// <summary>Runs protected execution text generation maintenance through independently authorized child requests.</summary>
    MaintainTextIndex = 31,
    /// <summary>Maintains an administrator-authorized online text generation through separate request grains.</summary>
    MaintainOnlineTextIndex = 34,
    /// <summary>Commits one freshly verified private online text publication; excluded from public operation factories.</summary>
    OnlineTextPublicationPhase = 35,
    /// <summary>Commits target-only inbox identity and effects without source acknowledgement.</summary>
    CommitInbox = 38,
    /// <summary>Commits a freshly verified private BM25 corpus publication; excluded from generic public factories.</summary>
    Bm25CorpusPublicationPhase = 45,
    /// <summary>Changes one existing queue pause state through an explicit schema-version CAS.</summary>
    SetQueueDispatch = 50,
    /// <summary>Atomically creates one absent canonical typed table with an original caller identity.</summary>
    CreateTable = 51,
    /// <summary>Execution all-owner table retirement and logical removal through the authenticated request grain.</summary>
    DropTable = 53,
    /// <summary>Changes metadata only through the authenticated file server.</summary>
    FileMetadata = 56,
}
