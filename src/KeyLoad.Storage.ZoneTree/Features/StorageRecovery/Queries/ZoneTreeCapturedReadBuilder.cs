using System.Runtime.CompilerServices;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Copies only admitted original snapshot scopes after sharing their original work and retained-buffer admission.</summary>
internal sealed class ZoneTreeCapturedReadBuilder(ZoneTreeReadCutLease original, Action<long, int> admit) : IScopedReadCapture
{
    private const int NoImportedRecords = 0;
    private const int EmptyScopeLength = 0;
    private const int FirstRecordIndex = 0;
    private const int NextRecordOffset = 1;
    private const int InitialCapacity = 1;
    private const int CapacityGrowthFactor = 2;
    private const long ArrayHeaderPointerSlots = 3;
    private const long KeyValueArrayHeaderPointerSlots = 6;
    private const string EmptyScope = "The captured scope must contain at least one key byte.";
    private const string DuplicatePoint = "The captured point has duplicate values.";
    private const string ConflictingValue = "The captured snapshot contains conflicting values.";

    private readonly List<ZoneTreeCapturedReadRecord> records = [];
    private readonly List<byte[]> prefixes = [];
    private readonly List<byte[]> exactKeys = [];
    private bool transferred;
    private int prefixBaseRecords;

    public void AdmitRetainedBytes(long bytes) => admit(bytes, NoImportedRecords);

    public void CapturePrefix(byte[] prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length == EmptyScopeLength)
        { throw new ArgumentException(EmptyScope, nameof(prefix)); }
        ObjectDisposedException.ThrowIf(transferred, this);
        if (Covers(prefix))
        { return; }
        ReserveScope(prefixes, prefix.Length);
        var ownedPrefix = prefix.ToArray();
        prefixBaseRecords = records.Count;
        original.VisitBorrowedPrefix(prefix, RetainPrefixRecord, AdmitPrefixRecord);
        records.Sort(static (left, right) => left.Key.Span.SequenceCompareTo(right.Key.Span));
        // A scope is complete only after native exhaustion, never on a stopped callback.
        prefixes.Add(ownedPrefix);
    }

    public void CaptureExact(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == EmptyScopeLength)
        { throw new ArgumentException(EmptyScope, nameof(key)); }
        ObjectDisposedException.ThrowIf(transferred, this);
        if (Covers(key) || exactKeys.Any(exact => exact.AsSpan().SequenceEqual(key)))
        { return; }
        ReserveScope(exactKeys, key.Length);
        var ownedKey = key.ToArray();
        original.ReadExact(key, value =>
        {
            ReserveRecord();
            admit(checked((long)ownedKey.Length + value.Length + KeyValueArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
            var location = ZoneTreeCapturedReadLookup.Find(records, ownedKey, records.Count);
            if (location >= FirstRecordIndex)
            { throw Errors.Fail(ErrorCode.Corruption, DuplicatePoint); }
            records.Insert(~location, new(ownedKey.ToArray(), value.ToArray()));
        }, admit);
        // Native absence is an explicitly observed key, not permission to query any other key.
        exactKeys.Add(ownedKey);
    }

    public bool ReadCapturedValue(byte[] key, StorageValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CaptureExact(key);
        var location = ZoneTreeCapturedReadLookup.Find(records, key, records.Count);
        if (location < FirstRecordIndex)
        { return false; }
        reader(records[location].Value.Span);
        return true;
    }

    public void VisitCapturedPrefix(byte[] prefix, StorageRecordVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        CapturePrefix(prefix);
        var first = ZoneTreeCapturedReadLookup.Find(records, prefix, records.Count);
        var index = first >= FirstRecordIndex ? first : ~first;
        while (index < records.Count)
        {
            var record = records[index];
            if (!record.Key.Span.StartsWith(prefix) || !visitor(record.Key.Span, record.Value.Span))
            { return; }
            // The callback may insert admitted dependency points; resume from the original owned key.
            index = ZoneTreeCapturedReadLookup.Find(records, record.Key.Span, records.Count) + NextRecordOffset;
        }
    }

    internal ZoneTreeCapturedReadView Transfer()
    {
        ObjectDisposedException.ThrowIf(transferred, this);
        original.ObserveSettledWork();
        transferred = true;
        return new(records, prefixes, exactKeys, admit);
    }

    internal void ClearUntransferred()
    {
        if (transferred)
        { return; }
        records.Clear();
        prefixes.Clear();
        exactKeys.Clear();
    }

    private bool RetainPrefixRecord(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var location = ZoneTreeCapturedReadLookup.Find(records, key, prefixBaseRecords);
        if (location >= FirstRecordIndex)
        {
            if (!records[location].Value.Span.SequenceEqual(value))
            { throw Errors.Fail(ErrorCode.Corruption, ConflictingValue); }
            return true;
        }
        records.Add(new(key.ToArray(), value.ToArray()));
        return true;
    }

    private void AdmitPrefixRecord(long bytes, int count)
    {
        admit(bytes, count);
        if (count == NoImportedRecords)
        { return; }
        ReserveRecord();
        // The owning captured-record visitor copies borrowed key/value only after this callback.
        // Raw bytes include the value header and conservatively charge that header again.
        admit(checked(bytes + KeyValueArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
    }

    private void ReserveRecord()
    {
        if (records.Count < records.Capacity)
        { return; }
        var next = Math.Max(InitialCapacity, checked(records.Capacity * CapacityGrowthFactor));
        admit(checked((long)Unsafe.SizeOf<ZoneTreeCapturedReadRecord>() * next + ArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
        records.Capacity = next;
    }

    private void ReserveScope(List<byte[]> scopes, int bytes)
    {
        admit(checked(bytes + ArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
        if (scopes.Count < scopes.Capacity)
        { return; }
        var next = Math.Max(InitialCapacity, checked(scopes.Capacity * CapacityGrowthFactor));
        admit(checked((long)IntPtr.Size * next + ArrayHeaderPointerSlots * IntPtr.Size), NoImportedRecords);
        scopes.Capacity = next;
    }

    private bool Covers(byte[] key)
    {
        foreach (var prefix in prefixes)
        { if (key.AsSpan().StartsWith(prefix)) { return true; } }
        return false;
    }
}
