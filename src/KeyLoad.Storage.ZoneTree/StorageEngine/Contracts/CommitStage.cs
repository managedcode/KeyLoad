namespace KeyLoad.Storage.ZoneTree;

/// <summary>Commit transitions exposed to deterministic recovery observers.</summary>
public enum CommitStage
{
    /// <summary>A transaction mutation has been staged in the ZoneTree transaction.</summary>
    MutationApplied,
    /// <summary>The ZoneTree transaction and position have reached the native process-persistent WAL.</summary>
    ApplyCompleted,
    /// <summary>ZoneTree's write-ahead log sync has made every commit through the observed position durable.</summary>
    WriteAheadLogSynced,
    /// <summary>Plain table data has reached the native process-persistent WAL before its transactional outcome.</summary>
    PlainWriteAheadLogPublished
}
