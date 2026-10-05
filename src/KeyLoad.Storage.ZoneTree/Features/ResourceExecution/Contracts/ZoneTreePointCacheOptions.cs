using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

/// <summary>Fixed limits for disposable embedded or exact local-binding positive point-value acceleration.</summary>
/// <param name="MemoryBudget">Externally owned, shared node retention budget.</param>
/// <remarks>Server RF3 admission requires the separate authenticated Orleans lease contract.</remarks>
public sealed record ZoneTreePointCacheOptions(ICacheMemoryBudget MemoryBudget)
{
    private int? maxEntriesOverride;
    private long? maxRetainedBytesOverride;
    private int? maxKeyBytesOverride;
    private int? maxValueBytesOverride;
    private int? maxPinsPerEntryOverride;
    private int? effectiveMaxEntries;
    private long? effectiveMaxRetainedBytes;
    private int? effectiveMaxKeyBytes;
    private int? effectiveMaxValueBytes;
    private int? effectiveMaxPinsPerEntry;
    private int? maximumVictimAttempts;
    private const string ExecutionPolicyRequired = "Point-cache execution policy must be resolved before fill admission.";
    /// <summary>Maximum live, retired-pinned and in-flight entry charges for this owner.</summary>
    public int MaxEntries
    {
        get => maxEntriesOverride ?? effectiveMaxEntries ?? ZoneTreePointCacheExecutionOptions.DefaultMaxEntries;
        init => maxEntriesOverride = value;
    }
    /// <summary>Maximum modeled bytes, including retained index capacity, for this owner.</summary>
    public long MaxRetainedBytes
    {
        get => maxRetainedBytesOverride ?? effectiveMaxRetainedBytes ?? ZoneTreePointCacheExecutionOptions.DefaultMaxRetainedBytes;
        init => maxRetainedBytesOverride = value;
    }
    /// <summary>Maximum encoded key length admitted to optional cache lookup.</summary>
    public int MaxKeyBytes
    {
        get => maxKeyBytesOverride ?? effectiveMaxKeyBytes ?? ZoneTreePointCacheExecutionOptions.DefaultMaxKeyBytes;
        init => maxKeyBytesOverride = value;
    }
    /// <summary>Maximum logical value length admitted to optional cache retention.</summary>
    public int MaxValueBytes
    {
        get => maxValueBytesOverride ?? effectiveMaxValueBytes ?? ZoneTreePointCacheExecutionOptions.DefaultMaxValueBytes;
        init => maxValueBytesOverride = value;
    }
    /// <summary>Maximum simultaneous borrowed readers of one retained entry.</summary>
    public int MaxPinsPerEntry
    {
        get => maxPinsPerEntryOverride ?? effectiveMaxPinsPerEntry ?? ZoneTreePointCacheExecutionOptions.DefaultMaxPinsPerEntry;
        init => maxPinsPerEntryOverride = value;
    }
    /// <summary>Fixed modeled charge retained until this owner's preallocated index is detached.</summary>
    public long IndexChargeBytes => ZoneTreePointCacheLedger.IndexCharge(MaxEntries);

    /// <summary>Rejects invalid limits before any store or cache files and arrays are opened.</summary>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(MemoryBudget);
        if (!ZoneTreePointCacheExecutionOptions.AreValid(MaxEntries, MaxRetainedBytes, MaxKeyBytes,
            MaxValueBytes, MaxPinsPerEntry, maximumVictimAttempts ?? ZoneTreePointCacheExecutionOptions.DefaultVictimAttempts))
        {
            throw new InvalidOperationException(ZoneTreePointCacheExecutionOptions.ValidationMessage);
        }
    }

    internal int MaximumVictimAttempts => maximumVictimAttempts ?? throw new InvalidOperationException(ExecutionPolicyRequired);

    /// <summary>Freezes native cache policy while preserving each explicit embedded descriptor override.</summary>
    /// <param name="options">The centrally validated native point-cache execution options.</param>
    /// <returns>Exact effective scalar cache budgets and the same borrowed shared-memory collaborator.</returns>
    public ZoneTreePointCacheOptions ResolveExecutionOptions(IOptions<ZoneTreePointCacheExecutionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value;
        ArgumentNullException.ThrowIfNull(configured);
        configured.Validate();
        return WithExecutionSnapshot(configured);
    }

    internal ZoneTreePointCacheOptions WithExecutionSnapshot(ZoneTreePointCacheExecutionOptions configured)
    {
        var resolved = this with { };
        resolved.effectiveMaxEntries = maxEntriesOverride ?? configured.MaxEntries;
        resolved.effectiveMaxRetainedBytes = maxRetainedBytesOverride ?? configured.MaxRetainedBytes;
        resolved.effectiveMaxKeyBytes = maxKeyBytesOverride ?? configured.MaxKeyBytes;
        resolved.effectiveMaxValueBytes = maxValueBytesOverride ?? configured.MaxValueBytes;
        resolved.effectiveMaxPinsPerEntry = maxPinsPerEntryOverride ?? configured.MaxPinsPerEntry;
        resolved.maximumVictimAttempts = configured.MaximumVictimAttempts;
        resolved.Validate();
        return resolved;
    }
}
