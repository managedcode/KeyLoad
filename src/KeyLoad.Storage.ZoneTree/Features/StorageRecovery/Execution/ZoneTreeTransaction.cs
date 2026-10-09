using System.Collections.Immutable;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeTransaction(ZoneTreeStoreRuntime runtime) : IAtomicTransaction
{
    private const int NoValueBytes = 0;
    private const int NoStagedBytes = 0;

    private StorageMutation[]? preparedChanges;
    private byte[]? preparedPayload;
    private long stagedBytes;

    internal SortedSet<ZoneTreeStagedEntry> Changes { get; } = new(ZoneTreeStagedEntryComparer.Instance);

    public byte[]? ReadOwnedValue(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Changes.TryGetValue(new(key, null), out var entry))
        {
            return runtime.View.ReadOwnedValue(key);
        }

        runtime.ReadCounters.Point(owned: true, (long)key.Length + (entry!.Value?.Length ?? NoValueBytes));
        return entry.Value?.ToArray();
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        if (!Changes.TryGetValue(new(key, null), out var staged))
        {
            return runtime.View.ReadValue(key, reader, observer);
        }

        var bytes = (long)key.Length + (staged!.Value?.Length ?? NoValueBytes);
        runtime.ReadCounters.Point(owned: false, bytes);
        observer?.Invoke(bytes);
        if (staged.Value is null)
        {
            return false;
        }

        reader(staged.Value);
        return true;
    }

    private static long MutationBytes(byte[] key, byte[]? value)
        => key.LongLength + (value?.LongLength ?? NoStagedBytes);

    private void Stage(byte[] key, byte[]? value)
    {
        var replacing = Changes.TryGetValue(new(key, null), out var previous);
        var bytes = stagedBytes - (replacing ? MutationBytes(key, previous!.Value) : NoStagedBytes)
            + MutationBytes(key, value);
        if (bytes > runtime.Options.MaxFrameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TransactionFrameLimitExceeded);
        }

        preparedPayload = null;
        preparedChanges = null;
        stagedBytes = bytes;
        if (replacing)
        {
            Changes.Remove(previous!);
        }

        Changes.Add(new(key.ToArray(), value?.ToArray()));
    }

    public void Put(byte[] key, byte[] value) => Stage(key, value);
    public void Delete(byte[] key) => Stage(key, null);

    public void Reset()
    {
        preparedPayload = null;
        preparedChanges = null;
        stagedBytes = NoStagedBytes;
        Changes.Clear();
    }

    public void ValidateCommit() => _ = PreparePayload();

    internal StorageMutation[] PrepareChanges()
        => preparedChanges ??= Changes.Select(pair => new StorageMutation(pair.Key,
            pair.Value is null ? (ReadOnlyMemory<byte>?)null : new ReadOnlyMemory<byte>(pair.Value))).ToArray();

    internal byte[] PreparePayload()
    {
        if (preparedPayload is not null)
        {
            return preparedPayload;
        }

        return preparedPayload = ZoneTreeJournalCodec.Serialize(PrepareChanges(), runtime.Options.MaxFrameBytes, runtime.Options.FaultObserver);
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
        => ZoneTreeRangeReader.Visit(runtime, prefix, maxRecords, visitor, Changes,
            afterKey, untilKey, observer, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => ZoneTreeRangeReader.Visit(runtime, prefix, maxRecords, visitor, Changes,
            afterKey, untilKey, observer, cancellationToken, reverse: true);
}
