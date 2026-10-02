namespace KeyLoad;

/// <summary>Identifies one blob within its complete atomic and resource scope.</summary>
/// <param name="Partition">The atomic partition, including its partition key.</param>
/// <param name="Resource">The configured binary resource.</param>
/// <param name="Id">The object identity within that scope.</param>
public sealed record BlobRef(PartitionRef Partition, string Resource, string Id);

/// <summary>Configures immutable version-one limits for one blob resource.</summary>
public sealed record BlobPolicy
{
    /// <summary>Gets the maximum declared raw length of one object.</summary>
    public long MaxBlobBytes { get; init; } = BlobLimits.DefaultMaxBlobBytes;
    /// <summary>Gets the reserved bytes for all staged, current and retired versions.</summary>
    public long MaxReservedBytes { get; init; } = BlobLimits.DefaultMaxReservedBytes;
    /// <summary>Gets the retained object identity limit, including tombstones.</summary>
    public int MaxObjectKeys { get; init; } = BlobLimits.DefaultMaxObjectKeys;
    /// <summary>Gets the limit of versions retained until reclaim completes.</summary>
    public int MaxVersions { get; init; } = BlobLimits.DefaultMaxVersions;
    /// <summary>Gets the maximum concurrent active uploads.</summary>
    public int MaxUploads { get; init; } = BlobLimits.DefaultMaxUploads;
    /// <summary>Gets the upload lifetime evaluated by the replicated command clock.</summary>
    public int UploadTtlSeconds { get; init; } = BlobLimits.DefaultUploadTtlSeconds;
}

/// <summary>Identifies the persisted public upload lifecycle.</summary>
public enum BlobUploadStatus
{
    /// <summary>The upload accepts ordered parts before its expiry.</summary>
    Active,
    /// <summary>The version was successfully published.</summary>
    Complete,
    /// <summary>The upload was explicitly or during restore aborted.</summary>
    Aborted,
    /// <summary>Replicated cleanup invalidated the expired upload.</summary>
    Expired
}
