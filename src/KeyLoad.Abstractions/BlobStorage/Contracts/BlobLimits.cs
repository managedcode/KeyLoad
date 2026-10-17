namespace KeyLoad;

/// <summary>Defines immutable version-one binary transfer and persisted capacity bounds.</summary>
public static class BlobLimits
{
    /// <summary>The required raw length of each nonfinal part.</summary>
    public const int RawPartBytes = 65_536;
    /// <summary>The inclusive maximum requested range length.</summary>
    public const int MaxRangeBytes = RawPartBytes;
    /// <summary>The maximum encoded bytes of one private head or upload-state record.</summary>
    public const int MaxMetadataRecordBytes = 16_384;
    /// <summary>The inclusive maximum declared object length.</summary>
    public const long MaximumBlobBytes = 1_073_741_824;
    /// <summary>The store-wide maximum reserved raw blob bytes.</summary>
    public const long StoreMaxReservedBytes = MaximumBlobBytes;
    /// <summary>The store-wide maximum retained object identity keys.</summary>
    public const int StoreMaxObjectKeys = 8_192;
    /// <summary>The store-wide maximum staged, published and retired versions.</summary>
    public const int StoreMaxVersions = 16_384;
    /// <summary>The store-wide maximum active uploads.</summary>
    public const int StoreMaxUploads = 1_024;
    /// <summary>The maximum parts deleted by one reclaim command.</summary>
    public const int MaxReclaimParts = 128;
    /// <summary>The maximum returned metadata rows in one listing.</summary>
    public const int MaxListItems = 100;
    /// <summary>The default per-resource maximum object length.</summary>
    public const long DefaultMaxBlobBytes = 67_108_864;
    /// <summary>The default per-resource reserved raw byte limit.</summary>
    public const long DefaultMaxReservedBytes = 268_435_456;
    /// <summary>The default per-resource retained identity key limit.</summary>
    public const int DefaultMaxObjectKeys = 4_096;
    /// <summary>The default per-resource retained version limit.</summary>
    public const int DefaultMaxVersions = 8_192;
    /// <summary>The default per-resource active upload limit.</summary>
    public const int DefaultMaxUploads = 128;
    /// <summary>The default upload lifetime in trusted evaluated seconds.</summary>
    public const int DefaultUploadTtlSeconds = 3_600;
    /// <summary>The minimum configured upload lifetime.</summary>
    public const int MinimumUploadTtlSeconds = 60;
    /// <summary>The maximum configured upload lifetime.</summary>
    public const int MaximumUploadTtlSeconds = 86_400;
}
