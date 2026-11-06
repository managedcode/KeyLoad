using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests;

/// <summary>Observes completed native point lookups while forwarding original bytes, readers and work charges.</summary>
internal sealed class PointObservedView(IKeyValueView original, Action afterRead) : IKeyValueView
{
    public int MaximumRangeRecords => original.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key)
    {
        var bytes = original.ReadOwnedValue(key);
        afterRead();
        return bytes;
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => original.ReadValue(key, bytes =>
        {
            afterRead();
            reader(bytes);
        }, observer);

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => original.Scan(prefix, maxRecords, afterKey);

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => original.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => original.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
}
