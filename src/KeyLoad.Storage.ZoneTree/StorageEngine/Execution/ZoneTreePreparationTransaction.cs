namespace KeyLoad.Storage.ZoneTree;

/// <summary>A callback-scoped real execution child; its inherited staged image and all changes are privately owned.</summary>
internal sealed class ZoneTreePreparationTransaction : IAtomicTransaction, IAtomicCommitCandidateValidation
{
    private const string OutsideCallback = "The execution preparation transaction is outside its synchronous owning callback.";
    private readonly ZoneTreeStoreRuntime runtime;
    private readonly ZoneTreeTransaction transaction;
    private readonly ZoneTreeStagedEntry[] inherited;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool active = true;

    internal ZoneTreePreparationTransaction(ZoneTreeStoreRuntime runtime, IEnumerable<ZoneTreeStagedEntry> inherited)
    {
        this.runtime = runtime;
        transaction = new(runtime, preparing: true);
        this.inherited = inherited.Select(row => new ZoneTreeStagedEntry(row.Key.ToArray(), row.Value?.ToArray())).ToArray();
        RestoreInherited();
    }

    internal T Run<T>(Func<IAtomicTransaction, T> prepare)
    {
        try
        {
            return prepare(this);
        }
        finally
        {
            active = false;
            transaction.EndPreparationContext();
            transaction.Reset();
            Array.Clear(inherited);
        }
    }

    private void Check()
    {
        if (!active || Environment.CurrentManagedThreadId != thread
            || !runtime.Gate.IsReadLockHeld && !runtime.Gate.IsWriteLockHeld && !runtime.Gate.IsUpgradeableReadLockHeld)
        {
            throw new InvalidOperationException(OutsideCallback);
        }
        runtime.Check();
    }

    public int MaximumRangeRecords
    {
        get
        {
            Check();
            return transaction.MaximumRangeRecords;
        }
    }

    public T Prepare<T>(Func<IAtomicTransaction, T> prepare)
    {
        Check();
        return ZoneTreeStorePreparationExecution.Fork(runtime, transaction.Changes, prepare);
    }

    public byte[]? ReadOwnedValue(byte[] key)
    {
        Check();
        return transaction.ReadOwnedValue(key);
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        Check();
        return transaction.ReadValue(key, reader, observer);
    }

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        Check();
        return transaction.Scan(prefix, maxRecords, afterKey);
    }

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        Check();
        return transaction.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
    }

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        Check();
        return transaction.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
    }

    public void Put(byte[] key, byte[] value)
    {
        Check();
        transaction.Put(key, value);
    }

    public void Delete(byte[] key)
    {
        Check();
        transaction.Delete(key);
    }

    public void Reset()
    {
        Check();
        transaction.Reset();
        RestoreInherited();
    }

    private void RestoreInherited()
    {
        foreach (var row in inherited)
        {
            if (row.Value is { } value)
            {
                transaction.Put(row.Key, value);
            }
            else
            {
                transaction.Delete(row.Key);
            }
        }
    }

    public void ValidateCommit()
    {
        Check();
        transaction.ValidateCommit();
    }

    public void ValidateCandidate(IReadOnlyList<StorageMutation> mutations)
    {
        Check();
        transaction.ValidateCandidate(mutations);
    }
}
