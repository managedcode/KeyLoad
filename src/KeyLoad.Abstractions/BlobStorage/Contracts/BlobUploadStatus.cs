namespace KeyLoad;

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
