using System.Collections.Immutable;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadView(ZoneTreeStoreRuntime runtime) : IKeyValueView
{
    public byte[]? ReadOwnedValue(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var admission = runtime.CacheLifecycle.CaptureReadAdmission();
        var generation = runtime.Identity.ReadGeneration;
        if (admission.TryPin(key, generation, out var entry))
        {
            try
            {
                runtime.ReadCounters.Point(owned: true, (long)key.Length + entry.Value.Length);
                return entry.Value.ToArray();
            }
            finally
            {
                admission.Unpin(entry);
            }
        }

        Memory<byte> memory = key;
        var found = runtime.Tree.TryGet(memory, out var value);
        admission.RecordNativeLookup(found);
        if (!found)
        {
            runtime.ReadCounters.Point(owned: true, key.Length);
            return null;
        }

        var borrowed = value.Span[StorageValueHeaderBytes..];
        runtime.ReadCounters.Point(owned: true, (long)key.Length + borrowed.Length);
        using var candidate = admission.TryPrepare(key, borrowed.Length, generation);
        if (candidate is not null)
        {
            admission.Publish(candidate, borrowed, generation);
        }

        return borrowed.ToArray();
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        var admission = runtime.CacheLifecycle.CaptureReadAdmission();
        var generation = runtime.Identity.ReadGeneration;
        if (admission.TryPin(key, generation, out var entry))
        {
            try
            {
                runtime.ReadCounters.Point(owned: false, (long)key.Length + entry.Value.Length);
                observer?.Invoke((long)key.Length + entry.Value.Length);
                reader(entry.Value);
                return true;
            }
            finally
            {
                admission.Unpin(entry);
            }
        }

        Memory<byte> memory = key;
        var found = runtime.Tree.TryGet(memory, out var value);
        admission.RecordNativeLookup(found);
        if (!found)
        {
            runtime.ReadCounters.Point(owned: false, key.Length);
            observer?.Invoke(key.Length);
            return false;
        }

        var borrowed = value.Span[StorageValueHeaderBytes..];
        runtime.ReadCounters.Point(owned: false, (long)key.Length + borrowed.Length);
        using var candidate = admission.TryPrepare(key, borrowed.Length, generation);
        observer?.Invoke((long)key.Length + borrowed.Length);
        if (candidate is not null)
        {
            admission.Publish(candidate, borrowed, generation);
        }

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

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => ZoneTreeRangeReader.Visit(runtime, prefix, maxRecords, visitor, null,
            afterKey, untilKey, observer, cancellationToken, reverse: true);
}
