using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests;

/// <summary>Observes completed native reads while forwarding original bytes, readers and work charges.</summary>
internal sealed class ReadObservedView(IKeyValueView original, Action? afterRead = null, Action? afterRange = null) : IKeyValueView
{
    public int MaximumRangeRecords => original.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key)
    {
        var bytes = original.ReadOwnedValue(key);
        afterRead?.Invoke();
        return bytes;
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => original.ReadValue(key, reader, bytes =>
        {
            observer?.Invoke(bytes);
            afterRead?.Invoke();
        });

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var page = original.Scan(prefix, maxRecords, afterKey);
        afterRange?.Invoke();
        return page;
    }

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => original.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey,
            bytes => ObserveRange(bytes, observer), cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => original.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey,
            bytes => ObserveRange(bytes, observer), cancellationToken);

    private void ObserveRange(long bytes, StorageReadObserver? observer)
    {
        observer?.Invoke(bytes);
        afterRange?.Invoke();
    }
}
