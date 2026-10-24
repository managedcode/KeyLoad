using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.StoragePartitions.Queries;

/// <summary>Reads the bounded resource definitions owned by one atomic partition.</summary>
internal static class PartitionResourceCatalog
{
    private const long NoObservedBytes = 0;
    private const string Inconsistent = "The partition resource catalog is inconsistent.";
    private const string Capacity = "The partition resource catalog exceeds its bounded capacity.";

    internal static ImmutableArray<ResourceDefinition> Capture(IKeyValueView view,
        PartitionRef partition, DatabaseLimits limits)
    {
        var resources = ImmutableArray.CreateBuilder<ResourceDefinition>();
        var bytes = NoObservedBytes;
        var scanned = view.VisitRange(KeySpace.ResourcePrefix(partition.TenantId, partition.DatabaseId),
            limits.MaxScanRecords, (key, value) =>
            {
                var definition = OrleansSerialization.Deserialize<ResourceDefinition>(value);
                if (!key.SequenceEqual(KeySpace.Resource(partition.TenantId, partition.DatabaseId, definition.Name)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, Inconsistent);
                }
                if (definition.TransactionDomainId == partition.TransactionDomainId)
                {
                    if (resources.Count >= limits.MaxBatchMutations)
                    {
                        throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
                    }
                    resources.Add(definition);
                }
                return true;
            }, observer: observed =>
            {
                if (observed > limits.MaxBatchBytes - bytes)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
                }
                bytes += observed;
            });
        if (scanned.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        var result = resources.ToImmutable();
        if (OrleansSerialization.Measure(result) > limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
        }
        return result;
    }
}
