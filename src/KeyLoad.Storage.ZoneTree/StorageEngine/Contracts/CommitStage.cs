namespace KeyLoad.Storage.ZoneTree;

/// <summary>Commit transitions exposed to deterministic recovery observers.</summary>
public enum CommitStage
{
    /// <summary>A transaction mutation has been staged in the ZoneTree transaction.</summary>
    MutationApplied,
    /// <summary>The ZoneTree transaction has committed and the position is published; it is visible, not yet durable.</summary>
    ApplyCompleted,
    /// <summary>ZoneTree's write-ahead log sync has made every commit through the observed position durable.</summary>
    WriteAheadLogSynced,
    /// <summary>Plain table data is durable; the transactional outcome has not been published yet.</summary>
    PlainWriteAheadLogSynced
}
