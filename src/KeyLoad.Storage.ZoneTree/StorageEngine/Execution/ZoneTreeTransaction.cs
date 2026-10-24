using System.Collections.Immutable;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeTransaction(ZoneTreeStoreRuntime runtime, bool preparing = false)
    : IAtomicTransaction, IAtomicCommitCandidateValidation, IAtomicActionStaging
{
    private const long NoPreparedVersion = -1;
    private readonly ZoneTreeTransactionStaging staging = new(runtime.Options.MaxFrameBytes);
    private StorageMutation[]? preparedChanges;
    private byte[]? preparedPayload;
    private long preparedVersion = NoPreparedVersion;
    private readonly int preparationThread = Environment.CurrentManagedThreadId;
    private bool preparationContextOpen = true;

    internal SortedSet<ZoneTreeStagedEntry> Changes => staging.Entries;

    public int MaximumRangeRecords => runtime.Options.MaximumRangeRecords;

    public bool FitsOneFrame => staging.FitsOneFrame;

    public void BeginAction() => staging.BeginAction();

    public T Prepare<T>(Func<IAtomicTransaction, T> prepare)
    {
        if (!preparationContextOpen || Environment.CurrentManagedThreadId != preparationThread)
        {
            throw new InvalidOperationException(ZoneTreeStorePreparationExecution.OutsideTransactionCallback);
        }
        var (earlier, action) = Inheritance;
        return ZoneTreeStorePreparationExecution.Fork(runtime, earlier, action, prepare, requireCommitGate: true);
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
            return preparing ? ZoneTreePreparationReads.ReadOwnedValue(runtime, key) : runtime.View.ReadOwnedValue(key);
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
            return preparing ? ZoneTreePreparationReads.ReadValue(runtime, key, reader, observer)
                : runtime.View.ReadValue(key, reader, observer);
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

    private static long MutationBytes(byte[] key, byte[]? value)
        => key.LongLength + (value?.LongLength ?? 0);

    public void Put(byte[] key, byte[] value) => staging.Stage(key, value);
    public void Delete(byte[] key) => staging.Stage(key, null);

    /// <summary>Discards the current action's staged writes; earlier actions keep theirs.</summary>
    public void Reset() => staging.ResetAction();

    public void ValidateCommit()
    {
        if (!staging.SharesFrame)
        {
            _ = PreparePayload();
            return;
        }

        _ = ZoneTreeJournalCodec.Serialize(ZoneTreeTransactionStaging.Mutations(staging.ActionEntries()),
            runtime.Options.MaxFrameBytes, preparing ? null : runtime.Options.FaultObserver);
    }

    public void ValidateCandidate(IReadOnlyList<StorageMutation> mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        var candidate = new SortedSet<ZoneTreeStagedEntry>(staging.ActionEntries(), ZoneTreeStagedEntryComparer.Instance);
        foreach (var mutation in mutations)
        {
            ArgumentNullException.ThrowIfNull(mutation);
            if (mutation.Key.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreeJournalCodec.JournalRecordsInvalid);
            }
            if (mutation.Key.Length > runtime.Options.MaxFrameBytes
                || (long)mutation.Key.Length + (mutation.Value?.Length ?? 0) > runtime.Options.MaxFrameBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TransactionFrameLimitExceeded);
            }
            var row = new ZoneTreeStagedEntry(mutation.Key.ToArray(), mutation.Value?.ToArray());
            candidate.Remove(row);
            candidate.Add(row);
        }
        if (candidate.Count == 0)
        {
            return;
        }
        long rawBytes = 0;
        foreach (var row in candidate)
        {
            rawBytes = checked(rawBytes + MutationBytes(row.Key, row.Value));
            if (rawBytes > runtime.Options.MaxFrameBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TransactionFrameLimitExceeded);
            }
        }
        var records = candidate.Select(row => new StorageMutation(row.Key,
            row.Value is null ? null : new ReadOnlyMemory<byte>(row.Value))).ToArray();
        _ = ZoneTreeJournalCodec.Serialize(records, runtime.Options.MaxFrameBytes);
    }

    /// <summary>Returns every staged write of all actions, in key order, for the one commit frame.</summary>
    internal StorageMutation[] PrepareChanges()
    {
        RetainPreparedVersion();
        return preparedChanges ??= ZoneTreeTransactionStaging.Mutations(Changes);
    }

    internal byte[] PreparePayload()
    {
        RetainPreparedVersion();
        if (preparedPayload is not null)
        {
            return preparedPayload;
        }

        return preparedPayload = ZoneTreeJournalCodec.Serialize(PrepareChanges(), runtime.Options.MaxFrameBytes, preparing ? null : runtime.Options.FaultObserver);
    }

    private void RetainPreparedVersion()
    {
        if (preparedVersion == staging.Version)
        {
            return;
        }

        preparedChanges = null;
        preparedPayload = null;
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
