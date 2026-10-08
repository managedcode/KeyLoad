using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.BackupRestore.Validation;
using KeyLoad.Core.Features.ClusterRouting.Authorization;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.BackupRestore.Execution;

internal sealed class AtomicPartitionRosterTransaction : IAtomicTransaction, IPartitionMovementReadScope
{
    private const int NoCandidates = 0;
    private readonly IAtomicTransaction inner;
    bool IPartitionMovementReadScope.MovementAuthorityAbsent { get; set; }
    private bool movementViewChanged;
    bool IPartitionMovementReadScope.CanReuseCommittedMovementAbsence => !movementViewChanged;
    bool IPartitionMovementReadScope.CanPublishCommittedMovementAbsence => false;
    private readonly IOptions<DatabaseLimits> limits;
    private readonly long storePosition;
    private readonly long appliedIndex;
    private readonly Dictionary<PartitionRef, byte[]> candidates = [];
    private const long NoRetainedKeyBytes = 0;
    private long retainedKeyBytes;
    private readonly record struct Candidate(PartitionRef Partition, byte[] Key);

    internal AtomicPartitionRosterTransaction(IAtomicTransaction inner, IOptions<DatabaseLimits> limits,
        long storePosition, long appliedIndex)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.limits = limits ?? throw new ArgumentNullException(nameof(limits));
        this.storePosition = storePosition;
        this.appliedIndex = appliedIndex > AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex
            ? appliedIndex : AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex;
    }

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
        ((IPartitionMovementReadScope)this).MovementAuthorityAbsent = false;
        movementViewChanged = true;
        var candidate = PrepareCandidate(key, value, hasValue: true);
        inner.Put(key, value);
        RetainCandidate(candidate);
    }

    public void Delete(byte[] key)
    {
        ((IPartitionMovementReadScope)this).MovementAuthorityAbsent = false;
        movementViewChanged = true;
        var candidate = PrepareCandidate(key, [], hasValue: false);
        inner.Delete(key);
        RetainCandidate(candidate);
    }

    public void Reset()
    {
        ((IPartitionMovementReadScope)this).MovementAuthorityAbsent = false;
        movementViewChanged = true;
        candidates.Clear();
        retainedKeyBytes = NoRetainedKeyBytes;
        inner.Reset();
    }

    public void ValidateCommit() => inner.ValidateCommit();

    internal void PersistCandidates()
    {
        if (candidates.Count == NoCandidates)
        {
            return;
        }

        var appliedBytes = inner.ReadOwnedValue(KeySpace.AppliedBytes);
        var currentAppliedIndex = appliedBytes is null
            ? AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex
            : NativeSerialization.Deserialize<long>(appliedBytes);
        foreach (var candidate in candidates)
        {
            var existing = inner.GetRecord<AtomicPartitionCatalogEntryV1>(candidate.Value);
            if (existing is not null)
            {
                AtomicPartitionRosterEntryValidation.Validate(existing, candidate.Key, storePosition, currentAppliedIndex);
                continue;
            }

            var firstSeenStorePosition = appliedIndex > AtomicPartitionRosterProtocol.NoReplicatedAppliedIndex
                ? AtomicPartitionRosterProtocol.NoReplicatedStorePosition : storePosition;
            var entry = new AtomicPartitionCatalogEntryV1(AtomicPartitionRosterProtocol.CurrentVersion,
                candidate.Key, firstSeenStorePosition, appliedIndex);
            var bytes = NativeSerialization.Serialize(entry);
            if (bytes.Length > limits.Value.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AtomicPartitionRosterProtocol.CandidateBudgetExhausted);
            }
            inner.Put(candidate.Value, bytes);
        }
    }

    private Candidate? PrepareCandidate(byte[] key, byte[] value, bool hasValue)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!AtomicPartitionRosterKeyValidation.TryReadCandidate(key, value, hasValue, out var partition)
            || partition is null)
        {
            return null;
        }

        var rosterKey = AtomicPartitionRosterKeys.Partition(partition);
        if (candidates.ContainsKey(partition))
        {
            return null;
        }
        if (candidates.Count >= limits.Value.MaxBatchMutations || rosterKey.Length > limits.Value.MaxBatchBytes
            || rosterKey.Length > limits.Value.MaxBatchBytes - retainedKeyBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AtomicPartitionRosterProtocol.CandidateBudgetExhausted);
        }
        return new(partition, rosterKey);
    }

    private void RetainCandidate(Candidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }
        var prepared = candidate.Value;
        candidates.Add(prepared.Partition, prepared.Key);
        retainedKeyBytes += prepared.Key.Length;
    }
}
