namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Centrally configured disposable cache retention, lookup, pin and eviction work limits.</summary>
[ConfigurationOptions]
public sealed record ZoneTreePointCacheExecutionOptions
{
    /// <summary>The local point-cache execution policy section.</summary>
    public const string SectionName = "KeyLoad:PointCache";
    /// <summary>The unchanged rejection for unsafe local point-cache budgets.</summary>
    public const string ValidationMessage = "The embedded point cache limits are outside their bounded domain.";
    /// <summary>The unchanged default retained entry count.</summary>
    public const int DefaultMaxEntries = 1_024;
    /// <summary>The unchanged default modeled retention budget.</summary>
    public const long DefaultMaxRetainedBytes = 16_777_216;
    /// <summary>The unchanged default encoded cache key budget.</summary>
    public const int DefaultMaxKeyBytes = 4_096;
    /// <summary>The unchanged default logical cache value budget.</summary>
    public const int DefaultMaxValueBytes = 65_536;
    /// <summary>The unchanged default simultaneous pin budget for one entry.</summary>
    public const int DefaultMaxPinsPerEntry = 1_024;
    internal const int DefaultVictimAttempts = 16;
    private const int MinimumPositiveBudget = 1;
    private const int MaximumEntryCapacity = 4_096;
    private const int MaximumKeyCapacity = 16_384;
    private const int MinimumValueCapacity = 0;
    private const int MaximumValueCapacity = 1_048_576;
    private const int MaximumPinCapacity = 4_096;
    private const long MaximumRetentionCapacity = 268_435_456;
    private const int MaximumEvictionWork = MaximumEntryCapacity;

    /// <summary>The maximum live, pinned-retired and in-flight entry charges.</summary>
    public int MaxEntries { get; init; } = DefaultMaxEntries;
    /// <summary>The maximum modeled bytes, including retained index capacity.</summary>
    public long MaxRetainedBytes { get; init; } = DefaultMaxRetainedBytes;
    /// <summary>The maximum encoded key length admitted to optional lookup.</summary>
    public int MaxKeyBytes { get; init; } = DefaultMaxKeyBytes;
    /// <summary>The maximum logical value length admitted to retention.</summary>
    public int MaxValueBytes { get; init; } = DefaultMaxValueBytes;
    /// <summary>The maximum simultaneously borrowed readers of one entry.</summary>
    public int MaxPinsPerEntry { get; init; } = DefaultMaxPinsPerEntry;
    /// <summary>The maximum retired victims examined for a single fill reservation.</summary>
    public int MaximumVictimAttempts { get; init; } = DefaultVictimAttempts;

    /// <summary>Checks the existing bounded cache domain and newly configurable eviction work.</summary>
    /// <returns>Whether every configured budget stays within the finite local cache domain.</returns>
    public bool IsValid() => AreValid(MaxEntries, MaxRetainedBytes, MaxKeyBytes, MaxValueBytes,
        MaxPinsPerEntry, MaximumVictimAttempts);

    internal static bool AreValid(int maxEntries, long maxRetainedBytes, int maxKeyBytes,
        int maxValueBytes, int maxPinsPerEntry, int maximumVictimAttempts)
        => maxEntries is >= MinimumPositiveBudget and <= MaximumEntryCapacity
            && maxKeyBytes is >= MinimumPositiveBudget and <= MaximumKeyCapacity
            && maxValueBytes is >= MinimumValueCapacity and <= MaximumValueCapacity
            && maxPinsPerEntry is >= MinimumPositiveBudget and <= MaximumPinCapacity
            && maxRetainedBytes >= ZoneTreePointCacheLedger.IndexCharge(maxEntries) + ZoneTreePointCacheState.EntryMetadataBytes
            && maxRetainedBytes <= MaximumRetentionCapacity
            && maximumVictimAttempts is >= MinimumPositiveBudget and <= MaximumEvictionWork;

    /// <summary>Rejects unsafe policy before allocating an index or reserving shared memory.</summary>
    public void Validate()
    {
        if (!IsValid())
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
