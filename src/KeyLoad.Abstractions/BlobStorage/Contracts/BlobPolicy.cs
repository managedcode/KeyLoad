namespace KeyLoad;

/// <summary>Configures immutable version-one limits for one blob resource.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobPolicy)]
public sealed record BlobPolicy
{
    /// <summary>Gets the maximum declared raw length of one object.</summary>
    [Orleans.Id(0)]
    public long MaxBlobBytes { get; init; } = BlobLimits.DefaultMaxBlobBytes;
    /// <summary>Gets the reserved bytes for all staged, current and retired versions.</summary>
    [Orleans.Id(1)]
    public long MaxReservedBytes { get; init; } = BlobLimits.DefaultMaxReservedBytes;
    /// <summary>Gets the retained object identity limit, including tombstones.</summary>
    [Orleans.Id(2)]
    public int MaxObjectKeys { get; init; } = BlobLimits.DefaultMaxObjectKeys;
    /// <summary>Gets the limit of versions retained until reclaim completes.</summary>
    [Orleans.Id(3)]
    public int MaxVersions { get; init; } = BlobLimits.DefaultMaxVersions;
    /// <summary>Gets the maximum concurrent active uploads.</summary>
    [Orleans.Id(4)]
    public int MaxUploads { get; init; } = BlobLimits.DefaultMaxUploads;
    /// <summary>Gets the upload lifetime evaluated by the replicated command clock.</summary>
    [Orleans.Id(5)]
    public int UploadTtlSeconds { get; init; } = BlobLimits.DefaultUploadTtlSeconds;
}
