using System.Collections.Immutable;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Reads logical bytes directly from the owning native snapshot and the plain data tree.</summary>
internal sealed class ZoneTreeReadView(ZoneTreeStoreRuntime runtime) : IKeyValueView
{
    public int MaximumRangeRecords => runtime.Options.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key) /* KLD0041: BCL null validation accepts the key byte[] reference through its object parameter. */;
        var found = Read(key, out var value);
        runtime.ReadCounters.Point(owned: true, (long)key.Length + (found ? value.Length : 0));
        return found ? value.ToArray() : null;
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key) /* KLD0041: BCL null validation accepts the key byte[] reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(reader) /* KLD0041: BCL null validation accepts the reader StorageValueReader reference through its object parameter. */;
        var found = Read(key, out var value);
        var bytes = (long)key.Length + (found ? value.Length : 0);
        runtime.ReadCounters.Point(owned: false, bytes);
        observer?.Invoke(bytes);
        if (found)
        {
            reader(value.Span);
        }
        return found;
    }

    private bool Read(byte[] key, out Memory<byte> value)
    {
        var snapshot = runtime.CurrentOperation.Snapshot.Transaction;
        if (!ZoneTreeDataRouting.IsPlain(runtime, key))
        {
            return snapshot.TryGet(key, out value);
        }
        var found = runtime.PlainTree.TryGet(key, out var plain);
        value = plain.Value;
        return found;
    }

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var records = ImmutableArray.CreateBuilder<KeyValueRecord>();
        var result = VisitRange(prefix, maxRecords, (key, value) =>
        {
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return new(records.ToImmutable(), result.HasMore);
    }

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => ZoneTreeRangeReader.Visit(runtime, prefix, maxRecords, visitor, null,
            afterKey, untilKey, observer, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => ZoneTreeRangeReader.Visit(runtime, prefix, maxRecords, visitor, null,
            afterKey, untilKey, observer, cancellationToken, reverse: true);
}
