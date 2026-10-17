namespace KeyLoad.Storage;

/// <summary>Reads a consistent view only inside its owning store action.</summary>
public interface IKeyValueView
{
    /// <summary>Gets the provider's actual accepted requested-record ceiling for forward range traversal.</summary>
    /// <remarks>Zero means range traversal is unsupported. This is not a cumulative work allowance;
    /// examined records, lookahead, bytes and lifetime remain bounded by their original owners.</remarks>
    int MaximumRangeRecords
    {
        get;
    }

    /// <summary>Copies a found record into an independently owned buffer.</summary>
    /// <param name="key">Encoded lookup key.</param>
    /// <returns>Owned logical bytes, or null when the key is absent or deleted.</returns>
    byte[]? ReadOwnedValue(byte[] key);
    /// <summary>Copies a bounded ordered page of matching logical records.</summary>
    /// <param name="prefix">Required encoded key prefix.</param>
    /// <param name="maxRecords">Maximum live records delivered before lookahead.</param>
    /// <param name="afterKey">Optional exclusive lower encoded key bound.</param>
    /// <returns>Owned key/value copies and whether another live record exists.</returns>
    ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null);

    /// <summary>Reads borrowed bytes inside the current storage gate, without an intermediate value copy.</summary>
    /// <param name="key">Owned lookup key supplied by the caller.</param>
    /// <param name="reader">Callback that cannot retain the borrowed span.</param>
    /// <param name="observer">Optional work charge before invoking the reader, including missing lookups.</param>
    /// <returns>True when a stored value was found.</returns>
    bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null);

    /// <summary>Visits ordered records inside the current storage gate and checks work before copying.</summary>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum logical records delivered before lookahead.</param>
    /// <param name="visitor">Read-only borrowed-record callback; false stops before another advance. Do not mutate the transaction from this callback.</param>
    /// <param name="afterKey">Optional exclusive lower key bound.</param>
    /// <param name="untilKey">Optional exclusive upper key bound.</param>
    /// <param name="observer">Optional examined-byte charge before callback or lookahead.</param>
    /// <param name="cancellationToken">Cancellation checked during traversal.</param>
    /// <returns>Delivered record count, stop reason and examined byte count.</returns>
    StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default);

    /// <summary>Visits descending encoded keys inside the same storage gate without materializing the range.</summary>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum live records delivered before charged lookahead.</param>
    /// <param name="visitor">Read-only borrowed callback; false stops before another advance. Do not mutate the transaction.</param>
    /// <param name="afterKey">Optional exclusive lower key bound, independent of direction.</param>
    /// <param name="untilKey">Optional exclusive upper key bound, independent of direction.</param>
    /// <param name="observer">Optional examined-byte charge before callback or lookahead.</param>
    /// <param name="cancellationToken">Cancellation checked during traversal.</param>
    /// <returns>Delivered count, stop reason and examined logical bytes.</returns>
    StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default);
}
