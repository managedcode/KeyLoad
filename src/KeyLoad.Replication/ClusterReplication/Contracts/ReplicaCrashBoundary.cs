namespace KeyLoad.Replication;

/// <summary>Identifies a durable boundary observed by real process-recovery tests.</summary>
public enum ReplicaCrashBoundary
{
    /// <summary>Term and vote have crossed the durable barrier.</summary>
    TermSaved,
    /// <summary>The ordered log suffix has crossed the durable barrier.</summary>
    EntryAcknowledged,
    /// <summary>The commit position has crossed the durable barrier.</summary>
    CommitAcknowledged,
    /// <summary>The complete incoming snapshot has been verified.</summary>
    SnapshotVerified,
    /// <summary>The verified canonical cut has been installed.</summary>
    SnapshotInstalled,
    /// <summary>The incoming transfer descriptor has been published.</summary>
    SnapshotTransferBegun,
    /// <summary>The incoming chunk has been durably acknowledged.</summary>
    SnapshotChunkAcknowledged,
    /// <summary>The incoming image failed verification before cleanup.</summary>
    SnapshotRejected,
    /// <summary>The installed image and replica snapshot metadata have been published.</summary>
    SnapshotPublished
}
