using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal static class PartitionMoveResources
{
    internal static ImmutableArray<ResourceDefinition> Capture(IKeyValueView view,
        PartitionRef partition, DatabaseLimits limits)
    {
        var resources = ImmutableArray.CreateBuilder<ResourceDefinition>();
        long bytes = PartitionMoveProtocol.EmptyCount;
        var scanned = view.VisitRange(KeySpace.ResourcePrefix(partition.TenantId, partition.DatabaseId),
            limits.MaxScanRecords, (key, value) =>
            {
                var definition = NativeSerialization.Deserialize<ResourceDefinition>(value);
                if (!key.SequenceEqual(KeySpace.Resource(partition.TenantId, partition.DatabaseId, definition.Name)))
                { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage); }
                if (definition.TransactionDomainId == partition.TransactionDomainId)
                {
                    if (resources.Count >= limits.MaxBatchMutations)
                    { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
                    resources.Add(definition);
                }
                return true;
            }, observer: observed =>
            {
                if (observed > limits.MaxBatchBytes - bytes)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
                bytes += observed;
            });
        if (scanned.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        var result = resources.ToImmutable();
        if (NativeSerialization.Measure(result) > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        return result;
    }
}
