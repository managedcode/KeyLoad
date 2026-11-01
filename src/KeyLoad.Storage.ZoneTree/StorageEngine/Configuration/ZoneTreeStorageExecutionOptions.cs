namespace KeyLoad.Storage.ZoneTree;

/// <summary>Centrally configured range work, read-cut budgets and store file buffers.</summary>
[ConfigurationOptions]
public sealed record ZoneTreeStorageExecutionOptions
{
    /// <summary>The storage execution configuration section.</summary>
    public const string SectionName = "KeyLoad:StorageExecution";
    /// <summary>The rejection for invalid storage execution policy.</summary>
    public const string ValidationMessage = "Storage execution budgets must be positive and read-cut durations must fit their supported range.";
    private const int DefaultMaximumRangeRecords = 100_000;
    private const long DefaultMaximumRangeWorkBytes = 67_108_864;
    private const int DefaultMaximumReadCutRecords = 5_000_000;
    private const long DefaultMaximumReadCutExaminedBytes = 1_073_741_824;
    private const int DefaultIdentityBufferBytes = 4_096;
    private const int DefaultStreamBufferBytes = 4_096;
    private const int DefaultMaximumIdentityFileBytes = 4_096;
    private const long MaximumTimerMilliseconds = 4_294_967_294;
    private static readonly TimeSpan MaximumTimerDuration = TimeSpan.FromMilliseconds(MaximumTimerMilliseconds);

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

    /// <summary>Maximum bytes used for IdentityBufferBytes during storage and offline operations.</summary>
    public int IdentityBufferBytes { get; init; } = DefaultIdentityBufferBytes;
    /// <summary>The execution buffer size of metadata file streams.</summary>
    public int StreamBufferBytes { get; init; } = DefaultStreamBufferBytes;
    /// <summary>Maximum bytes used for MaximumIdentityFileBytes during storage and offline operations.</summary>
    public int MaximumIdentityFileBytes { get; init; } = DefaultMaximumIdentityFileBytes;

    /// <summary>Checks positive budgets without inventing relationships between independently valid limits.</summary>
    /// <returns>Whether every budget can be consumed by the execution storage owners.</returns>
    public bool IsValid() => MaximumRangeRecords >= 1 && MaximumRangeWorkBytes >= 1
        && MaximumReadCutRecords >= 1 && MaximumReadCutExaminedBytes >= 1
        && MaximumReadCutElapsed > TimeSpan.Zero && MaximumReadCutElapsed <= MaximumTimerDuration
        && IdentityBufferBytes is >= 1 and <= DefaultIdentityBufferBytes
        && StreamBufferBytes is >= 1 and <= DefaultStreamBufferBytes
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
