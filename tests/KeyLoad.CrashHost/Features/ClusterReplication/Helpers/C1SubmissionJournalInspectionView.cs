using KeyLoad.Storage;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal sealed class C1SubmissionJournalInspectionView(IKeyValueView original,
    C1SubmissionJournalInspectionReadBudget budget, CancellationToken lifetime) : IKeyValueView
{
    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? value = null;
        ReadValue(key, borrowed => value = borrowed.ToArray());
        return value;
    }
    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        lifetime.ThrowIfCancellationRequested();
        return original.ReadValue(key, reader, bytes => { budget.Charge(bytes); observer?.Invoke(bytes); });
    }
    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        lifetime.ThrowIfCancellationRequested();
        cancellationToken.ThrowIfCancellationRequested();
        return original.VisitRange(prefix, maxRecords, (key, value) =>
        {
            lifetime.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            return visitor(key, value);
        }, afterKey, untilKey, bytes => { budget.Charge(bytes); observer?.Invoke(bytes); }, lifetime);
    }
    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => throw C1SubmissionJournalInspectionValidation.Invalid();
    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => throw C1SubmissionJournalInspectionValidation.Invalid();
}
