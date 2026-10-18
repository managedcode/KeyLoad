using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

/// <summary>Borrows frozen bytes captured at the original cut; never owns storage or live-cache publication.</summary>
internal sealed class NativeSearchCapturedReadScope : IKeyValueView
{
    private const long RetainedPointerSlots = 3;
    private readonly IKeyValueView captured;

    internal NativeSearchCapturedReadScope(IKeyValueView captured, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(budget);
        // Object header and one borrowed view reference.
        budget.ChargeBytes(checked(RetainedPointerSlots * IntPtr.Size));
        this.captured = captured;
    }

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
