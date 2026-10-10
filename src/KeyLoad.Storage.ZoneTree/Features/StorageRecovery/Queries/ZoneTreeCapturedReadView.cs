namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns bounded frozen bytes and complete admitted lookup scopes, never a live store view.</summary>
internal sealed class ZoneTreeCapturedReadView : IKeyValueView, IDisposable
{
    private const int NoImportedRecords = 0;
    private const int FirstRecordIndex = 0;
    private const int EmptyRecordCount = 0;
    private const int EqualKeyComparison = 0;
    private const int LastRecordOffset = 1;
    private const int InitialCapacity = 1;
    private const int CapacityGrowthFactor = 2;
    private const long ArrayHeaderPointerSlots = 3;
    private const long KeyValueArrayHeaderPointerSlots = 6;
    private const long RecordObjectHeaderPointerSlots = 2;
    private const long RecordMemoryFieldCount = 2;

    private const string Uncaptured = "The frozen read did not capture the requested scope.";
    private readonly List<ZoneTreeCapturedReadRecord> records;
    private readonly List<byte[]> prefixes;
    private readonly List<byte[]> exactKeys;
    private readonly Action<long, int> admitRetained;
    private bool disposed;

    internal ZoneTreeCapturedReadView(List<ZoneTreeCapturedReadRecord> records, List<byte[]> prefixes, List<byte[]> exactKeys, Action<long, int> admitRetained)
    {
        this.records = records;
        this.prefixes = prefixes;
        this.exactKeys = exactKeys;
        this.admitRetained = admitRetained;
        records.Sort(static (left, right) => left.Key.Span.SequenceCompareTo(right.Key.Span));
        exactKeys.Sort(BinaryKeyComparer.Instance);
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        RequireLookup(key);
        var location = ZoneTreeCapturedReadLookup.Find(records, key, records.Count);
        if (location >= FirstRecordIndex)
        {
            var record = records[location];
            observer?.Invoke(checked((long)key.Length + record.Value.Length));
            reader(record.Value.Span);
            return true;
        }
        observer?.Invoke(key.Length);
        return false;
    }

    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? result = null;
        ReadValue(key, value =>
        {
            admitRetained(checked(value.Length + ArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
            result = value.ToArray();
        });
        return result;
    }

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var result = new List<KeyValueRecord>();
        var scan = VisitRange(prefix, maxRecords, (key, value) =>
        {
            admitRetained(checked((long)key.Length + value.Length + KeyValueArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
            if (result.Count == result.Capacity)
            {
                var capacity = Math.Max(InitialCapacity, checked(result.Capacity * CapacityGrowthFactor));
                admitRetained(checked((long)IntPtr.Size * capacity + ArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
                result.Capacity = capacity;
            }
            admitRetained(checked(RecordObjectHeaderPointerSlots * IntPtr.Size + RecordMemoryFieldCount * System.Runtime.CompilerServices.Unsafe.SizeOf<ReadOnlyMemory<byte>>()), NoImportedRecords);
            result.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        admitRetained(checked((long)IntPtr.Size * result.Count + ArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
        return new([.. result], scan.HasMore);
    }

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => Visit(prefix, maxRecords, visitor, afterKey, untilKey, observer, false, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => Visit(prefix, maxRecords, visitor, afterKey, untilKey, observer, true, cancellationToken);

    private StorageScanResult Visit(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, bool reverse,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        RequireRange(prefix);
        var delivered = EmptyRecordCount;
        long examined = EmptyRecordCount;
        for (var offset = FirstRecordIndex; offset < records.Count; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = records[reverse ? records.Count - LastRecordOffset - offset : offset];
            if (!record.Key.Span.StartsWith(prefix)
                || afterKey is not null && record.Key.Span.SequenceCompareTo(afterKey) <= EqualKeyComparison
                || untilKey is not null && record.Key.Span.SequenceCompareTo(untilKey) >= EqualKeyComparison)
            { continue; }
            var bytes = checked((long)record.Key.Length + record.Value.Length);
            observer?.Invoke(bytes);
            examined = checked(examined + bytes);
            if (delivered == maxRecords)
            { return new(delivered, true, false, examined); }
            delivered++;
            if (!visitor(record.Key.Span, record.Value.Span))
            { return new(delivered, false, true, examined); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(delivered, false, false, examined);
    }

    private void RequireLookup(byte[] key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var prefix in prefixes)
        { if (key.AsSpan().StartsWith(prefix)) { return; } }
        if (exactKeys.BinarySearch(key, BinaryKeyComparer.Instance) >= EqualKeyComparison)
        { return; }
        throw Errors.Fail(ErrorCode.HistoryUnavailable, Uncaptured);
    }

    private void RequireRange(byte[] prefix)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (var admitted in prefixes)
        { if (prefix.AsSpan().StartsWith(admitted)) { return; } }
        throw Errors.Fail(ErrorCode.HistoryUnavailable, Uncaptured);
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        records.Clear();
        prefixes.Clear();
        exactKeys.Clear();
    }
}
