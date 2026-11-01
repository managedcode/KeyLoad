using System.Collections.Immutable;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeTransaction(ZoneTreeStoreRuntime runtime)
    : IAtomicTransaction, IAtomicActionStaging
{
    private const long NoPreparedVersion = -1;
    private readonly ZoneTreeTransactionStaging staging = new();
    private StorageMutation[]? preparedChanges;
    private long preparedVersion = NoPreparedVersion;
    private readonly int preparationThread = Environment.CurrentManagedThreadId;
    private bool preparationContextOpen = true;

    internal SortedSet<ZoneTreeStagedEntry> Changes => staging.Entries;

    public int MaximumRangeRecords => runtime.Options.MaximumRangeRecords;

    public void BeginAction() => staging.BeginAction();

    public T Prepare<T>(Func<IAtomicTransaction, T> prepare)
    {
        if (!preparationContextOpen || Environment.CurrentManagedThreadId != preparationThread)
        {
            throw new InvalidOperationException(ZoneTreeStorePreparationExecution.OutsideTransactionCallback);
        }
        var (earlier, action) = Inheritance;
        return ZoneTreeStorePreparationExecution.Fork(runtime, earlier, action, prepare, requireWriteTransaction: true);
    }

    /// <summary>Gets the current action's own entries and the entries of earlier actions it builds on.</summary>
    internal (IEnumerable<ZoneTreeStagedEntry> Earlier, IEnumerable<ZoneTreeStagedEntry> Action) Inheritance
        => (staging.EarlierEntries(), staging.ActionEntries());

    internal void EndPreparationContext() => preparationContextOpen = false;

    public byte[]? ReadOwnedValue(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Changes.TryGetValue(new(key, null), out var entry))
        {
            return runtime.View.ReadOwnedValue(key);
        }

        runtime.ReadCounters.Point(owned: true, (long)key.Length + (entry!.Value?.Length ?? 0));
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

        var bytes = (long)key.Length + (staged!.Value?.Length ?? 0);
        runtime.ReadCounters.Point(owned: false, bytes);
        observer?.Invoke(bytes);
        if (staged.Value is null)
        {
            return false;
        }

        reader(staged.Value);
        return true;
    }

    public void Put(byte[] key, byte[] value) => staging.Stage(key, value);
    public void Delete(byte[] key) => staging.Stage(key, null);

    /// <summary>Discards the current action's staged writes; earlier actions keep theirs.</summary>
    public void Reset() => staging.ResetAction();

    /// <summary>Returns every staged write of all actions, in key order, for the one ZoneTree commit.</summary>
    internal StorageMutation[] PrepareChanges()
    {
        RetainPreparedVersion();
        return preparedChanges ??= ZoneTreeTransactionStaging.Mutations(Changes);
    }

    private void RetainPreparedVersion()
    {
        if (preparedVersion == staging.Version)
        {
            return;
        }

        preparedChanges = null;
        preparedVersion = staging.Version;
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
