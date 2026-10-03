namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Fixed limits for disposable embedded or exact local-binding positive point-value acceleration.</summary>
/// <param name="MemoryBudget">Externally owned, shared node retention budget.</param>
/// <remarks>Server RF3 admission requires the separate authenticated Orleans lease contract.</remarks>
public sealed record ZoneTreePointCacheOptions(ICacheMemoryBudget MemoryBudget)
{
    private const string InvalidOptions = "The embedded point cache limits are outside their bounded domain.";
    /// <summary>Maximum live, retired-pinned and in-flight entry charges for this owner.</summary>
    public int MaxEntries { get; init; } = 1024;
    /// <summary>Maximum modeled bytes, including retained index capacity, for this owner.</summary>
    public long MaxRetainedBytes { get; init; } = 16L * 1024 * 1024;
    /// <summary>Maximum encoded key length admitted to optional cache lookup.</summary>
    public int MaxKeyBytes { get; init; } = 4096;
    /// <summary>Maximum logical value length admitted to optional cache retention.</summary>
    public int MaxValueBytes { get; init; } = 64 * 1024;
    /// <summary>Maximum simultaneous borrowed readers of one retained entry.</summary>
    public int MaxPinsPerEntry { get; init; } = 1024;
    /// <summary>Fixed modeled charge retained until this owner's preallocated index is detached.</summary>
    public long IndexChargeBytes => 1024L + (64L * MaxEntries);

    /// <summary>Rejects invalid limits before any store or cache files and arrays are opened.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(MemoryBudget);
        if (MaxEntries is < 1 or > 4096 || MaxKeyBytes is < 1 or > 16384
            || MaxValueBytes is < 0 or > 1024 * 1024 || MaxPinsPerEntry is < 1 or > 4096
            || MaxRetainedBytes < IndexChargeBytes + 320 || MaxRetainedBytes > 256L * 1024 * 1024)
        {
            throw new InvalidOperationException(InvalidOptions);
        }
    }
}
