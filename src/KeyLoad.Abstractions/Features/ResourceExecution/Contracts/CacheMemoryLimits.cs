namespace KeyLoad;

/// <summary>Bounds modeled retained cache bytes and live entries shared by one node.</summary>
/// <remarks>These admission ceilings do not bound process RSS, native storage or query working memory.</remarks>
public sealed record CacheMemoryLimits
{
    private const long DefaultBytes = 64L * 1024 * 1024;
    private const int DefaultEntries = 4096;
    private const long MaximumBytes = 1024L * 1024 * 1024;
    private const int MaximumEntries = 65536;
    private const string InvalidLimits = "Retained cache byte and entry limits must be positive and within supported bounds.";

    /// <summary>Maximum modeled retained bytes, including participating cache metadata charges.</summary>
    public long MaxRetainedBytes { get; init; } = DefaultBytes;

    /// <summary>Maximum entries, including evicted entries still pinned by an active reader.</summary>
    public int MaxRetainedEntries { get; init; } = DefaultEntries;

    /// <summary>Rejects invalid or unsupported admission ceilings before allocating cache state.</summary>
    public void Validate()
    {
        if (MaxRetainedBytes <= 0 || MaxRetainedBytes > MaximumBytes
            || MaxRetainedEntries <= 0 || MaxRetainedEntries > MaximumEntries)
        {
            throw new InvalidOperationException(InvalidLimits);
        }
    }
}
