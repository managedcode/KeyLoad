using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed class C1SubmissionOutcomeInspectionView(IKeyValueView original) : IKeyValueView
{
    private const int NoBytes = 0;
    private const int NoRangeRecords = 0;
    /// <summary>Range operations are unsupported by this point-only inspection view.</summary>
    public int MaximumRangeRecords => NoRangeRecords;
    private long examined;
    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? value = null;
        ReadValue(key, borrowed => value = borrowed.ToArray());
        return value;
    }
    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => original.ReadValue(key, reader, bytes => { Charge(bytes); observer?.Invoke(bytes); });
    private void Charge(long bytes)
    {
        if (bytes < NoBytes || bytes > C1SubmissionOutcomeInspectionProtocol.MaximumNativeRecordBytes
            || bytes > C1SubmissionOutcomeInspectionProtocol.MaximumReadBytes - examined)
        {
            throw C1SubmissionOutcomeInspectionValidation.Invalid();
        }
        examined += bytes;
    }
    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => throw C1SubmissionOutcomeInspectionValidation.Invalid();
    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => throw C1SubmissionOutcomeInspectionValidation.Invalid();
    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => throw C1SubmissionOutcomeInspectionValidation.Invalid();
}
