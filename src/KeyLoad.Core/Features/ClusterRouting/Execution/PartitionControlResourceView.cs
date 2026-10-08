using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal class PartitionControlResourceView : IKeyValueView
{
    private readonly IKeyValueView inner;
    private readonly ImmutableArray<(byte[] Key, byte[] Value)> resources;
    private readonly ReadExecutionBudget? work;
    private readonly ReadExecutionBudgetReadGrant? grant;

    internal PartitionControlResourceView(IKeyValueView inner, PartitionRef partition,
        ImmutableArray<ResourceDefinition> definitions, int maximumBytes, ReadExecutionBudget? work = null,
        ReadExecutionBudgetReadGrant? grant = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(partition);
        if (definitions.IsDefault || (work is null) != (grant is null))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        this.inner = inner;
        this.work = work;
        this.grant = grant;
        Partition = partition;
        var entries = ImmutableArray.CreateBuilder<(byte[] Key, byte[] Value)>(definitions.Length);
        long retained = PartitionMoveProtocol.EmptyCount;
        foreach (var definition in definitions)
        {
            var key = KeySpace.Resource(partition.TenantId, partition.DatabaseId, definition.Name);
            var value = NativeSerialization.Serialize(definition);
            retained = checked(retained + key.Length + value.Length);
            if (retained > maximumBytes)
            { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
            entries.Add((key, value));
        }
        resources = entries.MoveToImmutable();
    }

    internal PartitionRef Partition { get; }

    public byte[]? ReadOwnedValue(byte[] key)
    {
        byte[]? value = null;
        ReadValue(key, bytes => value = bytes.ToArray());
        return value;
    }

    public bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(reader);
        foreach (var resource in resources)
        {
            if (!key.AsSpan().SequenceEqual(resource.Key))
            { continue; }
            var count = checked(resource.Key.Length + resource.Value.Length);
            if (grant is not null)
            { work!.ChargeReadGrant(grant, count); }
            observer?.Invoke(count);
            reader(resource.Value);
            return true;
        }
        return inner.ReadValue(key, reader, observer);
    }

    public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        => inner.Scan(prefix, maxRecords, afterKey);
    public StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
    public StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default)
        => inner.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
}
