using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

internal class ResourceEffectObservationView(IKeyValueView inner, ResourceEffectBindingScope scope)
    : IKeyValueView, IResourceEffectObservation
{
    public void ObserveLifetime(ResourceLifetime lifetime) => scope.ObserveLifetime(lifetime);
    public void ObserveResource(PartitionRef partition, ResourceLifetime lifetime) => scope.Observe(partition, lifetime);
    public int MaximumRangeRecords => inner.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? result = null;
        ReadValue(key, bytes => result = bytes.ToArray());
        return result;
    }
    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
    {
        var rows = ImmutableArray.CreateBuilder<KeyValueRecord>();
        var scanned = VisitRange(prefix, maxRecords, (key, value) =>
        {
            rows.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, afterKey);
        return new(rows.ToImmutable(), scanned.HasMore);
    }
    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => inner.ReadValue(key, reader, observed => Charge(observed, observer));
    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey,
            observed => Charge(observed, observer), cancellationToken);
    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey,
            observed => Charge(observed, observer), cancellationToken);
    private void Charge(long bytes, StorageReadObserver? observer)
    {
        scope.ChargeRead(bytes);
        observer?.Invoke(bytes);
    }
}
