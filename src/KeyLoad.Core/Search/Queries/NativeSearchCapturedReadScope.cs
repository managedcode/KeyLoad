using KeyLoad.Core.Features.ClusterRouting.Authorization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

/// <summary>Borrows frozen bytes with their immutable original-cut movement proof; never owns storage or live-cache publication.</summary>
internal sealed class NativeSearchCapturedReadScope : IKeyValueView, IPartitionMovementReadScope
{
    private const long RetainedPointerSlots = 4;
    private const string ImmutableProof = "The captured movement-authority proof is immutable.";
    private readonly IKeyValueView captured;
    private readonly bool movementAuthorityAbsent;

    internal NativeSearchCapturedReadScope(IKeyValueView captured, NativeSearchReadAuthority authority,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(budget);
        // Object header, one borrowed view reference and the aligned immutable Boolean field.
        budget.ChargeBytes(checked(RetainedPointerSlots * IntPtr.Size));
        this.captured = captured;
        movementAuthorityAbsent = authority.MovementAuthorityAbsent;
    }

    bool IPartitionMovementReadScope.MovementAuthorityAbsent
    {
        get => movementAuthorityAbsent;
        set
        {
            if (value != movementAuthorityAbsent)
            {
                throw new InvalidOperationException(ImmutableProof);
            }
        }
    }
    bool IPartitionMovementReadScope.CanReuseCommittedMovementAbsence => false;
    bool IPartitionMovementReadScope.CanPublishCommittedMovementAbsence => false;

    public int MaximumRangeRecords => captured.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key) => captured.ReadOwnedValue(key);

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => captured.ReadValue(key, reader, observer);

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => captured.Scan(prefix, maxRecords, afterKey);

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => captured.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => captured.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
}
