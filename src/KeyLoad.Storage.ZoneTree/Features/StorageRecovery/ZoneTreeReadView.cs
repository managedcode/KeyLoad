using System.Collections.Immutable;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadView(ZoneTreeStoreRuntime runtime) : IKeyValueView
{
    public byte[]? ReadOwnedValue(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Memory<byte> memory = key;
        if (!runtime.Tree.TryGet(memory, out var value))
        {
            runtime.ReadCounters.Point(owned: true, key.Length);
            return null;
        }

        var borrowed = value.Span[StorageValueHeaderBytes..];
        runtime.ReadCounters.Point(owned: true, (long)key.Length + borrowed.Length);
        return borrowed.ToArray();
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        Memory<byte> memory = key;
        if (!runtime.Tree.TryGet(memory, out var value))
        {
            runtime.ReadCounters.Point(owned: false, key.Length);
            observer?.Invoke(key.Length);
            return false;
        }

        var borrowed = value.Span[StorageValueHeaderBytes..];
        runtime.ReadCounters.Point(owned: false, (long)key.Length + borrowed.Length);
        observer?.Invoke((long)key.Length + borrowed.Length);
        reader(borrowed);
        return true;
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
}
