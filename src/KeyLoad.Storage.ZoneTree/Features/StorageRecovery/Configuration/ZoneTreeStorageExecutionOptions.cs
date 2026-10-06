namespace KeyLoad.Storage.ZoneTree;

/// <summary>Centrally configured persistence, range work and native read-cut budgets.</summary>
[ConfigurationOptions]
public sealed record ZoneTreeStorageExecutionOptions
{
    /// <summary>The storage execution configuration section.</summary>
    public const string SectionName = "KeyLoad:StorageExecution";
    /// <summary>The rejection for invalid storage execution policy.</summary>
    public const string ValidationMessage = "Storage execution budgets must be positive and native read-cut deadlines must fit the native timer range.";
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
    private const int DefaultMaximumReadCutElapsedMinutes = 1;
    private const int DefaultFileBufferBytes = 65_536;
    private const int DefaultIdentityBufferBytes = 4_096;
    private const int DefaultStreamBufferBytes = 4_096;
    private const int DefaultMaximumBackupManifestBytes = 16_384;
    private const int DefaultMaximumIdentityFileBytes = 4_096;
    private const int DefaultMaximumUpgradeReceiptBytes = 65_536;
    private const int MinimumPositiveBudget = 1;
    private const long MaximumTimerMilliseconds = 4_294_967_294;
    private static readonly TimeSpan MaximumNativeTimerDuration = TimeSpan.FromMilliseconds(MaximumTimerMilliseconds);

    /// <summary>The maximum serialized journal frame size.</summary>
    public int MaxFrameBytes { get; init; } = DefaultMaxFrameBytes;
    /// <summary>The maximum complete snapshot file size.</summary>
    public long MaxSnapshotBytes { get; init; } = DefaultMaxSnapshotBytes;
    /// <summary>The payload byte threshold at which checkpoint batches are flushed.</summary>
    public int CheckpointBatchBytes { get; init; } = DefaultCheckpointBatchBytes;
    /// <summary>The record threshold at which checkpoint batches are flushed.</summary>
    public int CheckpointBatchRecords { get; init; } = DefaultCheckpointBatchRecords;
    /// <summary>The maximum delivered or examined records in a native range operation.</summary>
    public int MaximumRangeRecords { get; init; } = DefaultMaximumRangeRecords;
    /// <summary>The maximum provider bytes examined in one native range operation.</summary>
    public long MaximumRangeWorkBytes { get; init; } = DefaultMaximumRangeWorkBytes;
    /// <summary>The maximum admitted record budget for a captured native read cut.</summary>
    public int MaximumReadCutRecords { get; init; } = DefaultMaximumReadCutRecords;
    /// <summary>The maximum admitted provider byte budget for a captured native read cut.</summary>
    public long MaximumReadCutExaminedBytes { get; init; } = DefaultMaximumReadCutExaminedBytes;
    /// <summary>The maximum admitted duration of a captured native read cut.</summary>
    public TimeSpan MaximumReadCutElapsed { get; init; } = TimeSpan.FromMinutes(DefaultMaximumReadCutElapsedMinutes);

    /// <summary>Maximum bytes used for FileBufferBytes during storage and offline operations.</summary>
    public int FileBufferBytes { get; init; } = DefaultFileBufferBytes;
    /// <summary>Maximum bytes used for IdentityBufferBytes during storage and offline operations.</summary>
    public int IdentityBufferBytes { get; init; } = DefaultIdentityBufferBytes;
    /// <summary>The native buffer size of metadata, backup and checkpoint verification streams.</summary>
    public int StreamBufferBytes { get; init; } = DefaultStreamBufferBytes;
    /// <summary>Maximum bytes used for MaximumBackupManifestBytes during storage and offline operations.</summary>
    public int MaximumBackupManifestBytes { get; init; } = DefaultMaximumBackupManifestBytes;
    /// <summary>Maximum bytes used for MaximumIdentityFileBytes during storage and offline operations.</summary>
    public int MaximumIdentityFileBytes { get; init; } = DefaultMaximumIdentityFileBytes;
    /// <summary>Maximum bytes used for MaximumUpgradeReceiptBytes during storage and offline operations.</summary>
    public int MaximumUpgradeReceiptBytes { get; init; } = DefaultMaximumUpgradeReceiptBytes;

    /// <summary>Checks positive budgets without inventing relationships between independently valid limits.</summary>
    /// <returns>Whether every budget can be consumed by the native storage owners.</returns>
    public bool IsValid() => MaxFrameBytes >= MinimumPositiveBudget && MaxSnapshotBytes >= MinimumPositiveBudget
        && CheckpointBatchBytes >= MinimumPositiveBudget && CheckpointBatchRecords >= MinimumPositiveBudget
        && MaximumRangeRecords >= MinimumPositiveBudget && MaximumRangeWorkBytes >= MinimumPositiveBudget
        && MaximumReadCutRecords >= MinimumPositiveBudget && MaximumReadCutExaminedBytes >= MinimumPositiveBudget
        && MaximumReadCutElapsed > TimeSpan.Zero && MaximumReadCutElapsed <= MaximumNativeTimerDuration
        && FileBufferBytes is >= MinimumPositiveBudget and <= DefaultFileBufferBytes
        && IdentityBufferBytes is >= MinimumPositiveBudget and <= DefaultIdentityBufferBytes
        && StreamBufferBytes is >= MinimumPositiveBudget and <= DefaultStreamBufferBytes
        && MaximumBackupManifestBytes is >= MinimumPositiveBudget and <= DefaultMaximumBackupManifestBytes
        && MaximumIdentityFileBytes is >= MinimumPositiveBudget and <= DefaultMaximumIdentityFileBytes
        && MaximumUpgradeReceiptBytes is >= MinimumPositiveBudget and <= DefaultMaximumUpgradeReceiptBytes;

    /// <summary>Rejects invalid policy before opening files or admitting native work.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
