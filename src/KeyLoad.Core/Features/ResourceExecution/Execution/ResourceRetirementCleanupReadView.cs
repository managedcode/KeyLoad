using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Charges the immutable admitted apply grant without creating a new caller deadline.</summary>
internal sealed class ResourceRetirementCleanupReadView(IKeyValueView inner,
    long maximumBytes, int maximumRecords) : IKeyValueView
{
    private const int Step = 1;
    private const long NoReadBytes = 0;
    private const string Exceeded = "The admitted retirement cleanup read grant is exhausted.";
    internal long ExaminedBytes
    {
        get; private set;
    }
    internal int ExaminedRecords
    {
        get; private set;
    }
    internal long RemainingBytes => maximumBytes - ExaminedBytes;
    internal int RemainingRecords => maximumRecords - ExaminedRecords;

    private void Charge(long bytes)
    {
        if (bytes < NoReadBytes || bytes > RemainingBytes || RemainingRecords < Step)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
        }
        ExaminedBytes = checked(ExaminedBytes + bytes);
        ExaminedRecords = checked(ExaminedRecords + Step);
    }

    public int MaximumRangeRecords => inner.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? result = null;
        ReadValue(key, value => result = value.ToArray());
        return result;
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => inner.ReadValue(key, reader, bytes => { Charge(bytes); observer?.Invoke(bytes); });

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var rows = new List<KeyValueRecord>();
        var result = VisitRange(prefix, maxRecords, (key, value) =>
        {
            rows.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return new([.. rows], result.HasMore);
    }

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey,
            bytes => { Charge(bytes); observer?.Invoke(bytes); }, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey,
            bytes => { Charge(bytes); observer?.Invoke(bytes); }, cancellationToken);
}
