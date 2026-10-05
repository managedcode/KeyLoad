namespace KeyLoad.Storage.ZoneTree;

/// <summary>Configures the finite budgets and observer for explicit native3 snapshot conversion.</summary>
public sealed record ZoneTreeSnapshotUpgradeOptions
{
    internal int? MaxFrameBytesOverride { get; private init; }
    internal long? MaxSnapshotBytesOverride { get; private init; }
    /// <summary>Maximum serialized native checkpoint frame size.</summary>
    public int MaxFrameBytes
    {
        get => MaxFrameBytesOverride ?? ZoneTreeStorageExecutionOptions.DefaultMaxFrameBytes;
        init => MaxFrameBytesOverride = value;
    }

    /// <summary>Maximum complete native checkpoint size.</summary>
    public long MaxSnapshotBytes
    {
        get => MaxSnapshotBytesOverride ?? ZoneTreeStorageExecutionOptions.DefaultMaxSnapshotBytes;
        init => MaxSnapshotBytesOverride = value;
    }

    /// <summary>Observes the existing snapshot written and flushed boundaries.</summary>
    public Action<CommitStage, long, int>? FaultObserver { get; init; }
}
