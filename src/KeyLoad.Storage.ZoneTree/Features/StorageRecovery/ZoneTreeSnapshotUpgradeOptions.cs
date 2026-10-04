namespace KeyLoad.Storage.ZoneTree;

/// <summary>Configures the finite budgets and observer for explicit native3 snapshot conversion.</summary>
public sealed record ZoneTreeSnapshotUpgradeOptions
{
    /// <summary>Maximum serialized native checkpoint frame size.</summary>
    public int MaxFrameBytes { get; init; } = ZoneTreePersistenceFormat.DefaultMaxFrameBytes;

    /// <summary>Maximum complete native checkpoint size.</summary>
    public long MaxSnapshotBytes { get; init; } = ZoneTreePersistenceFormat.DefaultMaxSnapshotBytes;

    /// <summary>Observes the existing snapshot written and flushed boundaries.</summary>
    public Action<CommitStage, long, int>? FaultObserver { get; init; }
}
