using KeyLoad.Core.BackupRestore.Serialization;
using KeyLoad.Core.BackupRestore.Validation;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.BackupRestore.Execution;

internal sealed partial class AtomicPartitionRosterTransaction : IAtomicTransaction, IResourceEffectObservation, IResourceEffectCompletion
{
    private const int NoCandidates = 0;
    private readonly IAtomicTransaction inner;
    internal ResourceEffectBindingScope EffectAdmission
    {
        get;
    }
    private readonly long storePosition;
    private readonly long appliedIndex;
    private readonly Dictionary<PartitionRef, byte[]> candidates = [];
    private readonly record struct Candidate(PartitionRef Partition, byte[] Key);

    internal AtomicPartitionRosterTransaction(IAtomicTransaction inner,
        long storePosition, long appliedIndex)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        EffectAdmission = new();
        this.storePosition = storePosition;
        this.appliedIndex = appliedIndex > AtomicPartitionRosterProtocol.NoCanonicalSequence
            ? appliedIndex : AtomicPartitionRosterProtocol.NoCanonicalSequence;
    }

    public int MaximumRangeRecords => inner.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key) => inner.ReadOwnedValue(key);
    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => inner.Scan(prefix, maxRecords, afterKey);
    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => inner.ReadValue(key, reader, observer);
    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);

    public void Put(byte[] key, byte[] value)
    {
        var candidate = (PrepareCandidate(key)).Value;
        inner.Put(key, value);
        RetainCandidate(candidate);
    }

    public void Delete(byte[] key)
    {
        var candidate = (PrepareCandidate(key)).Value;
        inner.Delete(key);
        RetainCandidate(candidate);
    }

    public void Reset()
    {
        candidates.Clear();
        inner.Reset();
    }

    void IResourceEffectCompletion.RequireCompleteResourceEffects() => EffectAdmission.RequireComplete();

    public T Prepare<T>(Func<IAtomicTransaction, T> prepare)
        => AtomicPartitionRosterPreparation.Prepare(inner, storePosition, appliedIndex, prepare);

    public void ValidateCommit() => inner.ValidateCommit();

    void IResourceEffectObservation.ObserveLifetime(ResourceLifetime lifetime)
        => EffectAdmission.ObserveLifetime(lifetime);

    void IResourceEffectObservation.ObserveResource(PartitionRef partition, ResourceLifetime lifetime)
        => EffectAdmission.Observe(partition, lifetime);

    internal void PersistCandidates()
    {
        if (candidates.Count == NoCandidates)
        {
            return;
        }

        var appliedBytes = inner.ReadOwnedValue(KeySpace.AppliedBytes);
        var currentAppliedIndex = appliedBytes is null
            ? AtomicPartitionRosterProtocol.NoCanonicalSequence
            : OrleansSerialization.Deserialize<long>(appliedBytes);
        foreach (var candidate in candidates)
        {
            var existingBytes = inner.ReadOwnedValue(candidate.Value);
            var existing = existingBytes is null ? null : OrleansSerialization.Deserialize<AtomicPartitionCatalogEntry>(existingBytes);
            if (existing is not null)
            {
                AtomicPartitionRosterEntryValidation.Validate(existing, candidate.Key, storePosition,
                    currentAppliedIndex);
                continue;
            }

            if (existingBytes is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.InvalidEntry);
            }

            var firstSeenStorePosition = appliedIndex > AtomicPartitionRosterProtocol.NoCanonicalSequence
                ? AtomicPartitionRosterProtocol.NoStorePosition : storePosition;
            var entry = new AtomicPartitionCatalogEntry(AtomicPartitionRosterProtocol.CurrentVersion,
                candidate.Key, firstSeenStorePosition, appliedIndex);
            inner.Put(candidate.Value, OrleansSerialization.Serialize(entry));
        }
    }

    private global::ManagedCode.Communication.Result<Candidate?> PrepareCandidate(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!AtomicPartitionRosterKeyValidation.TryReadCandidate(key, out var partition)
            || partition is null)
        {
            return global::ManagedCode.Communication.Result<Candidate?>.FailNotFound(nameof(PrepareCandidate));
        }

        var rosterKey = AtomicPartitionRosterKeys.Partition(partition);
        var retained = candidates.ContainsKey(partition);
        return ((Candidate?)(retained ? null : new(partition, rosterKey))) is { } outcomeValue2 ? global::ManagedCode.Communication.Result<Candidate?>.Succeed(outcomeValue2) : global::ManagedCode.Communication.Result<Candidate?>.FailNotFound(nameof(PrepareCandidate));
    }

    private void RetainCandidate(Candidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }
        var prepared = candidate.Value;
        candidates.Add(prepared.Partition, prepared.Key);
    }
}
