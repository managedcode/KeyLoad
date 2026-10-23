namespace KeyLoad.Storage.ZoneTree;

/// <summary>Centrally configured persistence, range work and execution read-cut budgets.</summary>
[ConfigurationOptions]
public sealed record ZoneTreeStorageExecutionOptions
{
    /// <summary>The storage execution configuration section.</summary>
    public const string SectionName = "KeyLoad:StorageExecution";
    /// <summary>The rejection for invalid storage execution policy.</summary>
    public const string ValidationMessage = "Storage execution budgets must be positive; execution timer durations must fit their range and maintenance intervals must be at least one millisecond.";
    /// <summary>The unchanged default journal frame budget, also exposed by standalone store descriptors.</summary>
    public const int DefaultMaxFrameBytes = 33_554_432;
    /// <summary>The unchanged default complete snapshot budget.</summary>
    public const long DefaultMaxSnapshotBytes = 4_294_967_296;
    private const int DefaultCheckpointBatchBytes = 4_194_304;
    private const int DefaultCheckpointBatchRecords = 1_000;
    private const int DefaultMaximumRangeRecords = 100_000;
    private const long DefaultMaximumRangeWorkBytes = 67_108_864;
    private const int DefaultMaximumReadCutRecords = 5_000_000;
    private const long DefaultMaximumReadCutExaminedBytes = 1_073_741_824;
    private const int DefaultFileBufferBytes = 65_536;
    private const int DefaultIdentityBufferBytes = 4_096;
    private const int DefaultStreamBufferBytes = 4_096;
    private const int DefaultMaximumBackupManifestBytes = 16_384;
    private const int DefaultMaximumIdentityFileBytes = 4_096;
    private const int DefaultMaintenanceIntervalSeconds = 30;
    private const int DefaultBlockCacheLifetimeSeconds = 60;
    private const long MaximumTimerMilliseconds = 4_294_967_294;
    private static readonly TimeSpan MaximumTimerDuration = TimeSpan.FromMilliseconds(MaximumTimerMilliseconds);

    /// <summary>The maximum serialized journal frame size.</summary>
    public int MaxFrameBytes { get; init; } = DefaultMaxFrameBytes;
    /// <summary>The maximum complete snapshot file size.</summary>
    public long MaxSnapshotBytes { get; init; } = DefaultMaxSnapshotBytes;
    /// <summary>The payload byte threshold at which checkpoint batches are flushed.</summary>
    public int CheckpointBatchBytes { get; init; } = DefaultCheckpointBatchBytes;
    /// <summary>The record threshold at which checkpoint batches are flushed.</summary>
    public int CheckpointBatchRecords { get; init; } = DefaultCheckpointBatchRecords;
    /// <summary>The maximum delivered or examined records in a execution range operation.</summary>
    public int MaximumRangeRecords { get; init; } = DefaultMaximumRangeRecords;
    /// <summary>The maximum provider bytes examined in one execution range operation.</summary>
    public long MaximumRangeWorkBytes { get; init; } = DefaultMaximumRangeWorkBytes;
    /// <summary>The maximum admitted record budget for a captured execution read cut.</summary>
    public int MaximumReadCutRecords { get; init; } = DefaultMaximumReadCutRecords;
    /// <summary>The maximum admitted provider byte budget for a captured execution read cut.</summary>
    public long MaximumReadCutExaminedBytes { get; init; } = DefaultMaximumReadCutExaminedBytes;
    /// <summary>The maximum admitted duration of a captured execution read cut.</summary>
    public TimeSpan MaximumReadCutElapsed { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The interval of the physically owned execution cache-cleanup task.</summary>
    public TimeSpan MaintenanceInterval { get; init; } = TimeSpan.FromSeconds(DefaultMaintenanceIntervalSeconds);
    /// <summary>The inactive execution block-buffer lifetime before cleanup.</summary>
    public TimeSpan BlockCacheLifetime { get; init; } = TimeSpan.FromSeconds(DefaultBlockCacheLifetimeSeconds);

    /// <summary>Maximum bytes used for FileBufferBytes during storage and offline operations.</summary>
    public int FileBufferBytes { get; init; } = DefaultFileBufferBytes;
    /// <summary>Maximum bytes used for IdentityBufferBytes during storage and offline operations.</summary>
    public int IdentityBufferBytes { get; init; } = DefaultIdentityBufferBytes;
    /// <summary>The execution buffer size of metadata, backup and checkpoint verification streams.</summary>
    public int StreamBufferBytes { get; init; } = DefaultStreamBufferBytes;
    /// <summary>Maximum bytes used for MaximumBackupManifestBytes during storage and offline operations.</summary>
    public int MaximumBackupManifestBytes { get; init; } = DefaultMaximumBackupManifestBytes;
    /// <summary>Maximum bytes used for MaximumIdentityFileBytes during storage and offline operations.</summary>
    public int MaximumIdentityFileBytes { get; init; } = DefaultMaximumIdentityFileBytes;

    /// <summary>Checks positive budgets without inventing relationships between independently valid limits.</summary>
    /// <returns>Whether every budget can be consumed by the execution storage owners.</returns>
    public bool IsValid() => MaxFrameBytes >= 1 && MaxSnapshotBytes >= 1
        && CheckpointBatchBytes >= 1 && CheckpointBatchRecords >= 1
        && MaximumRangeRecords >= 1 && MaximumRangeWorkBytes >= 1
        && MaximumReadCutRecords >= 1 && MaximumReadCutExaminedBytes >= 1
        && MaximumReadCutElapsed > TimeSpan.Zero && MaximumReadCutElapsed <= MaximumTimerDuration
        && MaintenanceInterval.TotalMilliseconds >= 1 && MaintenanceInterval <= MaximumTimerDuration
        && BlockCacheLifetime >= TimeSpan.Zero && BlockCacheLifetime <= MaximumTimerDuration
        && FileBufferBytes is >= 1 and <= DefaultFileBufferBytes
        && IdentityBufferBytes is >= 1 and <= DefaultIdentityBufferBytes
        && StreamBufferBytes is >= 1 and <= DefaultStreamBufferBytes
        && MaximumBackupManifestBytes is >= 1 and <= DefaultMaximumBackupManifestBytes
        && MaximumIdentityFileBytes is >= 1 and <= DefaultMaximumIdentityFileBytes;

    /// <summary>Rejects invalid policy before opening files or admitting execution work.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }

}
