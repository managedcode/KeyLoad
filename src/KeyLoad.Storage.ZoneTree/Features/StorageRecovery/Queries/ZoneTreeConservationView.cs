using System.Collections.Immutable;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Charges native borrowed lookups and traversal, including missing lookups and lookahead.</summary>
internal sealed class ZoneTreeConservationView(IKeyValueView source, ZoneTreeConservationBudget budget) : IKeyValueView
{
    internal ZoneTreeConservationBudget Budget => budget;
    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? value = null;
        ReadValue(key, bytes => value = bytes.ToArray());
        return value;
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => source.ReadValue(key, reader, bytes => Charge(bytes, observer));

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var records = ImmutableArray.CreateBuilder<KeyValueRecord>();
        var page = VisitRange(prefix, maxRecords, (key, value) =>
        {
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return new(records.ToImmutable(), page.HasMore);
    }

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => source.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey,
            bytes => Charge(bytes, observer), cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => source.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey,
            bytes => Charge(bytes, observer), cancellationToken);

    private void Charge(long bytes, StorageReadObserver? observer)
    {
        budget.Charge(bytes);
        observer?.Invoke(bytes);
    }
}
