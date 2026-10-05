namespace KeyLoad.Replication;

/// <summary>Resource admission limits for inspecting and converting a stopped physical node.</summary>
[ConfigurationOptions]
public sealed record OfflineRecoveryExecutionOptions
{
    /// <summary>The centrally bound section for offline recovery work.</summary>
    public const string SectionName = "KeyLoad:OfflineRecovery";
    /// <summary>The failure raised before file inspection when a configured resource limit is invalid.</summary>
    public const string ValidationMessage = "Offline recovery inventory limits must be positive.";

    private const int DefaultMaximumSnapshotImages = 1_024;
    private const long DefaultMaximumSnapshotInventoryBytes = 68_719_476_736;
    private const int MinimumSnapshotImages = 1;
    private const long MinimumSnapshotInventoryBytes = 1;

    /// <summary>Gets the maximum admitted immutable replica snapshot images, including inventory work.</summary>
    public int MaximumSnapshotImages { get; init; } = DefaultMaximumSnapshotImages;
    /// <summary>Gets the maximum cumulative source or converted replica image bytes.</summary>
    public long MaximumSnapshotInventoryBytes { get; init; } = DefaultMaximumSnapshotInventoryBytes;

    /// <summary>Checks independent positive inventory and byte limits.</summary>
    public bool IsValid() => MaximumSnapshotImages >= MinimumSnapshotImages
        && MaximumSnapshotInventoryBytes >= MinimumSnapshotInventoryBytes;

    /// <summary>Rejects invalid limits before file inspection or output creation.</summary>
    public void Validate()
    {
        if (!IsValid())
        { throw new InvalidOperationException(ValidationMessage); }
    }
}
