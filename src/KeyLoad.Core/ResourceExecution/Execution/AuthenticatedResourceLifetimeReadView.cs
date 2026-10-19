using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution.Execution;

/// <summary>Forwards an already charged local view while keeping authenticated source pairs separate from local cursor pairs.</summary>
internal sealed class AuthenticatedResourceLifetimeReadView(IKeyValueView view, ReadExecutionBudgetLifetimeBindings binding)
    : IKeyValueView, IResourceLifetimeReadBindingView
{
    void IResourceLifetimeReadBindingView.BindResourceLifetime(PartitionRef partition,
        ResourceDefinition definition, ResourceLifetime lifetime)
        => binding.ObserveAuthenticated(partition, lifetime);

    public int MaximumRangeRecords => view.MaximumRangeRecords;

    public byte[]? ReadOwnedValue(byte[] key) => view.ReadOwnedValue(key);

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
        => view.ReadValue(key, reader, observer);

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => view.Scan(prefix, maxRecords, afterKey);

    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => view.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);

    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => view.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
}
