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
    /// <summary>The native encoder rejected a prefix: long is attempted bytes and int is the unchanged frame cap.</summary>
    EncodedFrameRejected
}
