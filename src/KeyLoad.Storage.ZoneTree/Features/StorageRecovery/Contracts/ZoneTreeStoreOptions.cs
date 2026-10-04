using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Durable journal and snapshot transitions exposed to deterministic recovery observers.</summary>
public enum CommitStage
{
    /// <summary>The journal frame header has been written.</summary>
    HeaderWritten,
    /// <summary>The journal payload has been written.</summary>
    PayloadWritten,
    /// <summary>The complete journal frame has been flushed.</summary>
    JournalFlushed,
    /// <summary>A transaction mutation has been applied to the materialized tree.</summary>
    MutationApplied,
    /// <summary>All transaction mutations have been applied and the position published.</summary>
    ApplyCompleted,
    /// <summary>A snapshot payload has been written.</summary>
    SnapshotWritten,
    /// <summary>The complete snapshot has been flushed.</summary>
    SnapshotFlushed,
    /// <summary>Snapshot installation has entered its prepared state.</summary>
    InstallPrepared,
    /// <summary>The canonical journal file has been swapped during snapshot replacement.</summary>
    JournalSwapped,
    /// <summary>The complete native5 source identity and journal passed the offline preflight.</summary>
    UpgradeSourceVerified,
    /// <summary>The checksummed migration receipt and exact source copies are durable in private staging.</summary>
    UpgradePrepared,
    /// <summary>The private current6 tree recovered the complete source checkpoint and journal tail.</summary>
    UpgradeRecovered,
    /// <summary>The current checkpoint4 image has been completely flushed in private staging.</summary>
    UpgradeCheckpointFlushed,
    /// <summary>The verified current6 staging directory was atomically published.</summary>
    UpgradePublished
}

/// <summary>Configures one node-local ZoneTree store and its bounded persistence budgets.</summary>
/// <param name="Directory">Private directory that owns the journal, identity and materialized tree.</param>
public sealed record ZoneTreeStoreOptions(string Directory)
{
    /// <summary>Optional stable node incarnation required when reopening an existing store.</summary>
    public Guid? Incarnation { get; init; }
    /// <summary>Optional private signing key; caller bytes are copied before becoming store identity.</summary>
    public ReadOnlyMemory<byte>? SigningKey { get; init; }
    /// <summary>Optional observer invoked at named durable state transitions.</summary>
    public Action<CommitStage, long, int>? FaultObserver { get; init; }
    /// <summary>Maximum serialized journal frame size.</summary>
    public int MaxFrameBytes { get; init; } = ZoneTreePersistenceFormat.DefaultMaxFrameBytes;
    /// <summary>Maximum complete snapshot file size.</summary>
    public long MaxSnapshotBytes { get; init; } = ZoneTreePersistenceFormat.DefaultMaxSnapshotBytes;
    /// <summary>Explicit embedded-only cache opt-in with an externally owned shared node budget.</summary>
    /// <remarks>RF3 server composition stays cold until authenticated Orleans coordination is enabled.</remarks>
    public ZoneTreePointCacheOptions? EmbeddedPointCache { get; init; }
}
