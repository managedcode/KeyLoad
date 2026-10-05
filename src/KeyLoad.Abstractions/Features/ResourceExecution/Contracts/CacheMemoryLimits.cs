namespace KeyLoad;

/// <summary>Bounds modeled retained cache bytes and live entries shared by one node.</summary>
/// <remarks>These admission ceilings do not bound process RSS, native storage or query working memory.</remarks>
[ConfigurationOptions]
public sealed record CacheMemoryLimits
{
    /// <summary>The section bound by the server composition root.</summary>
    public const string SectionName = "KeyLoad:CacheMemory";
    private const long NoRetainedBytes = 0;
    private const int NoRetainedEntries = 0;
    private const long DefaultBytes = 64L * 1024 * 1024;
    private const int DefaultEntries = 4096;
    private const long MaximumBytes = 1024L * 1024 * 1024;
    private const int MaximumEntries = 65536;
    /// <summary>The startup rejection for invalid cache memory limits.</summary>
    public const string ValidationMessage = "Retained cache byte and entry limits must be positive and within supported bounds.";

    /// <summary>Maximum modeled retained bytes, including participating cache metadata charges.</summary>
    public long MaxRetainedBytes { get; init; } = DefaultBytes;

    /// <summary>Maximum entries, including evicted entries still pinned by an active reader.</summary>
    public int MaxRetainedEntries { get; init; } = DefaultEntries;

    /// <summary>Rejects invalid or unsupported admission ceilings before allocating cache state.</summary>
    public void Validate()
    {
        if (MaxRetainedBytes <= NoRetainedBytes || MaxRetainedBytes > MaximumBytes
            || MaxRetainedEntries <= NoRetainedEntries || MaxRetainedEntries > MaximumEntries)
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}
